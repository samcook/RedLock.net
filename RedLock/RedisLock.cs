using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using RedLock.Logging;
using RedLock.Util;
using StackExchange.Redis;

namespace RedLock
{
	public class RedisLock : IDisposable
	{
		private readonly object lockObject = new object();

		private readonly ICollection<RedisConnection> redisCaches;
		private readonly IRedLockLogger logger;

		private readonly int quorum;
		private readonly int quorumRetryCount;
		private readonly int quorumRetryDelayMs;
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
		private const string ExtendIfMatchingValueScript =
			@"local currentVal = redis.call('get', KEYS[1])
			if (currentVal == false) then
				return redis.call('set', KEYS[1], ARGV[1], 'EX', ARGV[2])
			elseif (currentVal == ARGV[1]) then
				return redis.call('expire', KEYS[1], ARGV[2])
			else
				return -1
			end";

		public readonly string Resource;
		public readonly string LockId;
		public bool IsAcquired { get; private set; }
		public int ExtendCount { get; private set; }
		private readonly TimeSpan expiryTime;
		private readonly TimeSpan? waitTime;
		private readonly TimeSpan? retryTime;

		public Func<string, string> DefaultRedisKeyFormatter
		{
			get { return s => string.Format("redlock-{0}", s); }
		}

		internal RedisLock(
			ICollection<RedisConnection> redisCaches,
			string resource,
			TimeSpan expiryTime,
			TimeSpan? waitTime = null,
			TimeSpan? retryTime = null,
			Func<string, string> redisKeyFormatter = null,
			IRedLockLogger logger = null)
		{
			this.redisCaches = redisCaches;
			var formatter = redisKeyFormatter ?? DefaultRedisKeyFormatter;
			this.logger = logger ?? new NullLogger();

			quorum = redisCaches.Count() / 2 + 1;
			quorumRetryCount = 3;
			quorumRetryDelayMs = 400;
			clockDriftFactor = 0.01;
			redisKey = formatter(resource);

			Resource = resource;
			LockId = Guid.NewGuid().ToString();
			this.expiryTime = expiryTime;
			this.waitTime = waitTime;
			this.retryTime = retryTime;

			Start();
		}

		private void Start()
		{
			if (waitTime.HasValue && retryTime.HasValue && waitTime.Value.TotalMilliseconds > 0 && retryTime.Value.TotalMilliseconds > 0)
			{
				var endTime = DateTime.UtcNow + waitTime.Value;

				while (!IsAcquired && DateTime.UtcNow <= endTime)
				{
					IsAcquired = Acquire();

					Thread.Sleep(retryTime.Value);
				}
			}
			else
			{
				IsAcquired = Acquire();
			}

			if (IsAcquired)
			{
				StartAutoExtendTimer();
			}
		}

		private bool Acquire()
		{
			for (var i = 0; i < quorumRetryCount; i++)
			{
				logger.DebugWrite("Lock attempt {0} of {1}: {2}, {3}", i + 1, quorumRetryCount, Resource, expiryTime);

				var startTick = DateTime.UtcNow.Ticks;

				var locksAcquired = Lock();

				var validityTicks = GetRemainingValidityTicks(startTick);

				logger.DebugWrite("Acquired locks for id {0} in {1} of {2} instances, quorum is {3}, validityTicks is {4}", LockId, locksAcquired, redisCaches.Count(), quorum, validityTicks);

				if (locksAcquired >= quorum && validityTicks > 0)
				{
					return true;
				}
				
				// we failed to get enough locks for a quorum, unlock everything and try again
				Unlock();

				// only sleep if we have more retries left
				if (i < quorumRetryCount - 1)
				{
					var sleepMs = ThreadSafeRandom.Next(quorumRetryDelayMs);

					logger.DebugWrite("Sleeping {0}ms", sleepMs);

					Thread.Sleep(sleepMs);
				}
			}

			// give up
			logger.DebugWrite("Could not get lock for id {0}, giving up", LockId);

			return false;
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
				(long) expiryTime.TotalMilliseconds/2,
				(long) expiryTime.TotalMilliseconds/2);
		}

		private long GetRemainingValidityTicks(long startTick)
		{
			// Add 2 milliseconds to the drift to account for Redis expires precision,
			// which is 1 milliescond, plus 1 millisecond min drift for small TTLs.
			var driftTicks = ((long) (expiryTime.Ticks*clockDriftFactor)) + TimeSpan.FromMilliseconds(2).Ticks;
			var validityTicks = expiryTime.Ticks - ((DateTime.UtcNow.Ticks) - startTick) - driftTicks;
			return validityTicks;
		}

		private int Lock()
		{
			var locksAcquired = 0;

			Parallel.ForEach(redisCaches, cache =>
			{
				if (LockInstance(cache))
				{
					Interlocked.Increment(ref locksAcquired);
				}
			});

			return locksAcquired;
		}

		private int Extend()
		{
			var locksExtended = 0;

			Parallel.ForEach(redisCaches, cache =>
			{
				if (ExtendInstance(cache))
				{
					Interlocked.Increment(ref locksExtended);
				}
			});

			return locksExtended;
		}

		private void Unlock()
		{
			Parallel.ForEach(redisCaches, cache => UnlockInstance(cache));

			IsAcquired = false;
		}

		private bool LockInstance(RedisConnection cache)
		{
			var host = GetHost(cache.ConnectionMultiplexer);

			var result = false;

			try
			{
				logger.DebugWrite("LockInstance enter {0}: {1}, {2}, {3}", host, redisKey, LockId, expiryTime);
				result = cache.ConnectionMultiplexer
					.GetDatabase(cache.RedisDatabase)
					.StringSet(redisKey, LockId, expiryTime, When.NotExists, CommandFlags.DemandMaster);
			}
			catch (Exception ex)
			{
				logger.DebugWrite("Error locking lock instance {0}: {1}", host, ex.Message);
			}

			logger.DebugWrite("LockInstance exit {0}: {1}, {2}, {3}", host, redisKey, LockId, result);

			return result;
		}

		private bool ExtendInstance(RedisConnection cache)
		{
			var host = GetHost(cache.ConnectionMultiplexer);

			var result = false;

			try
			{
				logger.DebugWrite("ExtendInstance enter {0}: {1}, {2}, {3}", host, redisKey, LockId, expiryTime);
				var extendResult = (long) cache.ConnectionMultiplexer
					.GetDatabase(cache.RedisDatabase)
					.ScriptEvaluate(ExtendIfMatchingValueScript, new RedisKey[] {redisKey}, new RedisValue[] {LockId, (int) expiryTime.TotalSeconds}, CommandFlags.DemandMaster);

				result = (extendResult == 1);
			}
			catch (Exception ex)
			{
				logger.DebugWrite("Error extending lock instance {0}: {1}", host, ex.Message);
			}

			logger.DebugWrite("ExtendInstance exit {0}: {1}, {2}, {3}", host, redisKey, LockId, result);

			return result;
		}

		private bool UnlockInstance(RedisConnection cache)
		{
			var host = GetHost(cache.ConnectionMultiplexer);

			var result = false;

			try
			{
				logger.DebugWrite("UnlockInstance enter {0}: {1}, {2}", host, redisKey, LockId);
				result = (bool) cache.ConnectionMultiplexer
					.GetDatabase(cache.RedisDatabase)
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
