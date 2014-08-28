using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using RedLock.Logging;
using StackExchange.Redis;

namespace RedLock
{
	public class RedisLock : IDisposable
	{
		private static readonly Random Rand = new Random();
		private readonly object lockObject = new object();

		private readonly ICollection<ConnectionMultiplexer> redisCaches;
		private readonly IRedLockLogger logger;

		private readonly int quorum;
		private readonly int retryCount;
		private readonly int retryDelayMs;
		private readonly double clockDriftFactor;
		private readonly string redisKey;
		private bool isDisposed;

		private Timer lockKeepaliveTimer;

		private const string UnlockScript =
			@"if redis.call('get', KEYS[1]) == ARGV[1] then
				return redis.call('del', KEYS[1])
			else
				return 0
			end";

		// Set the expiry for the given key if its value matches the supplied value.
		// Returns 1 on success, 0 on failure setting expiry or key not existing, -1 if the key value didn't match
		private const string SetExpiryIfMatchingValueScript =
			@"local currentVal = redis.call('get', KEYS[1])
			if (currentVal == false) then
				return 0
			elseif (currentVal == ARGV[1]) then
				return redis.call('expire', KEYS[1], ARGV[2])
			else
				return -1
			end";

		public readonly string Resource;
		public readonly string LockId;
		public readonly TimeSpan ExpiryTime;
		public bool IsAcquired { get; private set; }
		public int ExtendCount { get; private set; }

		public Func<string, string> DefaultRedisKeyFormatter
		{
			get { return s => string.Format("redlock-{0}", s); }
		}

		public RedisLock(
			ICollection<ConnectionMultiplexer> redisCaches,
			string resource,
			TimeSpan expiryTime,
			Func<string, string> redisKeyFormatter = null,
			IRedLockLogger logger = null)
		{
			this.redisCaches = redisCaches;
			var formatter = redisKeyFormatter ?? DefaultRedisKeyFormatter;
			this.logger = logger ?? new NullLogger();

			quorum = redisCaches.Count() / 2 + 1;
			retryCount = 3;
			retryDelayMs = 400;
			clockDriftFactor = 0.01;
			redisKey = formatter(resource);

			Resource = resource;
			LockId = Guid.NewGuid().ToString();
			ExpiryTime = expiryTime;

			Start();

			if (IsAcquired)
			{
				// start auto-extend timer
				StartAutoExtendTimer();
			}
		}

		private void Start()
		{
			for (var i = 0; i < retryCount; i++)
			{
				logger.DebugWrite("Lock attempt {0} of {1}: {2}, {3}", i + 1, retryCount, Resource, ExpiryTime);

				var startTick = DateTime.UtcNow.Ticks;

				var locksAcquired = Lock();

				var validityTicks = GetRemainingValidityTicks(startTick);

				logger.DebugWrite("Acquired locks for id {0} in {1} of {2} instances, quorum is {3}, validityTicks is {4}", LockId, locksAcquired, redisCaches.Count(), quorum, validityTicks);

				if (locksAcquired >= quorum && validityTicks > 0)
				{
					IsAcquired = true;
				
					return;
				}
				
				// we failed to get enough locks for a quorum, unlock everything and try again
				Unlock();

				// only sleep if we have more retries left
				if (i < retryCount - 1)
				{
					var sleepMs = Rand.Next(retryDelayMs);

					logger.DebugWrite("Sleeping {0}ms", sleepMs);

					Thread.Sleep(sleepMs);
				}
			}

			// give up
			logger.DebugWrite("Could not get lock for id {0}, giving up", LockId);
		}

		private void StartAutoExtendTimer()
		{
			logger.DebugWrite("Starting auto extend timer");

			lockKeepaliveTimer = new Timer(
				state =>
				{
					try
					{
						logger.DebugWrite("Lock renewal timer fired for resource '{0}', lockId '{1}'", Resource, LockId);

						var extendSuccess = false;

						if (IsAcquired)
						{
							var startTick = DateTime.UtcNow.Ticks;

							var locksExtended = Extend();

							var validityTicks = GetRemainingValidityTicks(startTick);

							if (locksExtended >= quorum && validityTicks > 0)
							{
								extendSuccess = true;
								ExtendCount++;
							}
						}

						lock (lockObject)
						{
							IsAcquired = extendSuccess;

							if (!IsAcquired)
							{
								if (lockKeepaliveTimer != null)
								{
									// Stop the timer
									lockKeepaliveTimer.Change(Timeout.Infinite, Timeout.Infinite);
								}
							}
						}
					}
					catch (Exception exception)
					{
						// All we can do here is log the exception and swallow it.
						var message = string.Format("Lock renewal timer thread failed for resource '{0}', lockId '{1}'", Resource, LockId);
						logger.ErrorWrite(message, exception);
					}
				},
				null,
				(long) ExpiryTime.TotalMilliseconds/2,
				(long) ExpiryTime.TotalMilliseconds/2);
		}

		private long GetRemainingValidityTicks(long startTick)
		{
			// Add 2 milliseconds to the drift to account for Redis expires precision,
			// which is 1 milliescond, plus 1 millisecond min drift for small TTLs.
			var driftTicks = ((long) (ExpiryTime.Ticks*clockDriftFactor)) + TimeSpan.FromMilliseconds(2).Ticks;
			var validityTicks = ExpiryTime.Ticks - ((DateTime.UtcNow.Ticks) - startTick) - driftTicks;
			return validityTicks;
		}

		private int Lock()
		{
			var locksAcquired = 0;

			foreach (var cache in redisCaches)
			{
				if (LockInstance(cache))
				{
					locksAcquired++;
				}
			}
			
			return locksAcquired;
		}

		private int Extend()
		{
			var locksExtended = 0;

			foreach (var cache in redisCaches)
			{
				if (ExtendInstance(cache))
				{
					locksExtended++;
				}
			}

			return locksExtended;
		}

		private void Unlock()
		{
			foreach (var cache in redisCaches)
			{
				UnlockInstance(cache);
			}

			IsAcquired = false;
		}

		private bool LockInstance(ConnectionMultiplexer cache)
		{
			var host = GetHost(cache);

			var result = false;

			try
			{
				logger.DebugWrite("LockInstance enter {0}: {1}, {2}, {3}", host, redisKey, LockId, ExpiryTime);
				result = cache.GetDatabase().StringSet(redisKey, LockId, ExpiryTime, When.NotExists, CommandFlags.DemandMaster);
			}
			catch (Exception ex)
			{
				logger.DebugWrite("Error locking lock instance {0}: {1}", host, ex.Message);
			}

			logger.DebugWrite("LockInstance exit {0}: {1}, {2}, {3}", host, redisKey, LockId, result);

			return result;
		}

		private bool ExtendInstance(ConnectionMultiplexer cache)
		{
			var host = GetHost(cache);

			var result = false;

			try
			{
				logger.DebugWrite("ExtendInstance enter {0}: {1}, {2}, {3}", host, redisKey, LockId, ExpiryTime);
				var extendResult = (long) cache.GetDatabase()
					.ScriptEvaluate(SetExpiryIfMatchingValueScript, new RedisKey[] {redisKey}, new RedisValue[] {LockId, (int) ExpiryTime.TotalSeconds}, CommandFlags.DemandMaster);

				result = (extendResult == 1);
			}
			catch (Exception ex)
			{
				logger.DebugWrite("Error extending lock instance {0}: {1}", host, ex.Message);
			}

			logger.DebugWrite("ExtendInstance exit {0}: {1}, {2}, {3}", host, redisKey, LockId, result);

			return result;
		}

		private bool UnlockInstance(ConnectionMultiplexer cache)
		{
			var host = GetHost(cache);

			var result = false;

			try
			{
				logger.DebugWrite("UnlockInstance enter {0}: {1}, {2}", host, redisKey, LockId);
				result = (bool) cache.GetDatabase()
					.ScriptEvaluate(UnlockScript, new RedisKey[] {redisKey}, new RedisValue[] {LockId}, CommandFlags.DemandMaster);
			}
			catch (Exception ex)
			{
				logger.DebugWrite("Error unlocking lock instance {0}: {1}", host, ex.Message);
			}

			logger.DebugWrite("UnlockInstance exit {0}: {1}, {2}, {3}", host, redisKey, LockId, result);

			return result;
		}

		internal static string GetHost(ConnectionMultiplexer cache)
		{
			var endPoint = cache.GetEndPoints()[0];

			var dnsEndPoint = endPoint as DnsEndPoint;

			if (dnsEndPoint != null)
			{
				return String.Format("{0}:{1}", dnsEndPoint.Host, dnsEndPoint.Port);
			}

			var ipEndPoint = endPoint as IPEndPoint;

			if (ipEndPoint != null)
			{
				return String.Format("{0}:{1}", ipEndPoint.Address, ipEndPoint.Port);
			}

			return endPoint.ToString();
		}

		public void Dispose()
		{
			Dispose(true);
		}

		protected virtual void Dispose(bool disposing)
		{
			logger.DebugWrite("Disposing {0} {1}", Resource, LockId);

			if (isDisposed)
			{
				return;
			}

			if (disposing)
			{
				lock (lockObject)
				{
					if (lockKeepaliveTimer != null)
					{
						lockKeepaliveTimer.Change(Timeout.Infinite, Timeout.Infinite);
						lockKeepaliveTimer.Dispose();
						lockKeepaliveTimer = null;
					}
				}
			}

			Unlock();

			isDisposed = true;
		}

		/// <summary>
		/// For unit tests only, do not use in normal operation
		/// </summary>
		internal void StopKeepAliveTimer()
		{
			if (lockKeepaliveTimer == null)
			{
				return;
			}

			logger.DebugWrite("Stopping auto extend timer");

			lockKeepaliveTimer.Change(Timeout.Infinite, Timeout.Infinite);
		}
	}
}
