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
		private static readonly ILog Logger = LogProvider.GetCurrentClassLogger();

		private readonly int quorum;
		private readonly int quorumRetryCount;
		private readonly int quorumRetryDelayMs;
		private readonly double clockDriftFactor;
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
				return redis.call('set', KEYS[1], ARGV[1], 'PX', ARGV[2]) and 1 or 0
			elseif (currentVal == ARGV[1]) then
				return redis.call('pexpire', KEYS[1], ARGV[2])
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

		public const string DefaultRedisKeyFormat = "redlock-{0}";

		private RedisLock(
			ICollection<RedisConnection> redisCaches,
			string resource,
			TimeSpan expiryTime,
			TimeSpan? waitTime = null,
			TimeSpan? retryTime = null)
		{
			this.redisCaches = redisCaches;

			quorum = redisCaches.Count() / 2 + 1;
			quorumRetryCount = 3;
			quorumRetryDelayMs = 400;
			clockDriftFactor = 0.01;

			Resource = resource;
			LockId = Guid.NewGuid().ToString();
			this.expiryTime = expiryTime;
			this.waitTime = waitTime;
			this.retryTime = retryTime;
		}

		internal static RedisLock Create(
			ICollection<RedisConnection> redisCaches,
			string resource,
			TimeSpan expiryTime,
			TimeSpan? waitTime = null,
			TimeSpan? retryTime = null)
		{
			var redisLock = new RedisLock(
				redisCaches,
				resource,
				expiryTime,
				waitTime,
				retryTime);

			redisLock.Start();

			return redisLock;
		}

		internal static async Task<RedisLock> CreateAsync(
			ICollection<RedisConnection> redisCaches,
			string resource,
			TimeSpan expiryTime,
			TimeSpan? waitTime = null,
			TimeSpan? retryTime = null)
		{
			var redisLock = new RedisLock(
				redisCaches,
				resource,
				expiryTime,
				waitTime,
				retryTime);

			await redisLock.StartAsync().ConfigureAwait(false);

			return redisLock;
		}

		private void Start()
		{
			if (waitTime.HasValue && retryTime.HasValue && waitTime.Value.TotalMilliseconds > 0 && retryTime.Value.TotalMilliseconds > 0)
			{
				var endTime = DateTime.UtcNow + waitTime.Value;

				while (!IsAcquired && DateTime.UtcNow <= endTime)
				{
					IsAcquired = Acquire();

					if (!IsAcquired)
					{
						Thread.Sleep(retryTime.Value);
					}
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

		private async Task StartAsync()
		{
			if (waitTime.HasValue && retryTime.HasValue && waitTime.Value.TotalMilliseconds > 0 && retryTime.Value.TotalMilliseconds > 0)
			{
				var endTime = DateTime.UtcNow + waitTime.Value;

				while (!IsAcquired && DateTime.UtcNow <= endTime)
				{
					IsAcquired = await AcquireAsync().ConfigureAwait(false);

					if (!IsAcquired)
					{
						await TaskUtils.Delay(retryTime.Value).ConfigureAwait(false);
					}
				}
			}
			else
			{
				IsAcquired = await AcquireAsync().ConfigureAwait(false);
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
				var iteration = i + 1;
				Logger.Debug(() => $"Lock attempt {iteration} of {quorumRetryCount}: {Resource}, {expiryTime}");

				var startTick = DateTime.UtcNow.Ticks;

				var locksAcquired = Lock();

				var validityTicks = GetRemainingValidityTicks(startTick);

				Logger.Debug(() => $"Acquired locks for id {LockId} in {locksAcquired} of {redisCaches.Count()} instances, quorum is {quorum}, validityTicks is {validityTicks}");

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

					Logger.Debug(() => $"Sleeping {sleepMs}ms");

					Thread.Sleep(sleepMs);
				}
			}

			// give up
			Logger.Debug(() => $"Could not get lock for id {LockId}, giving up");

			return false;
		}

		private async Task<bool> AcquireAsync()
		{
			for (var i = 0; i < quorumRetryCount; i++)
			{
				var iteration = i + 1;
				Logger.Debug(() => $"Lock attempt {iteration} of {quorumRetryCount}: {Resource}, {expiryTime}");

				var startTick = DateTime.UtcNow.Ticks;

				var locksAcquired = await LockAsync().ConfigureAwait(false);

				var validityTicks = GetRemainingValidityTicks(startTick);

				Logger.Debug(() => $"Acquired locks for id {LockId} in {locksAcquired} of {redisCaches.Count()} instances, quorum is {quorum}, validityTicks is {validityTicks}");

				if (locksAcquired >= quorum && validityTicks > 0)
				{
					return true;
				}

				// we failed to get enough locks for a quorum, unlock everything and try again
				await UnlockAsync().ConfigureAwait(false);

				// only sleep if we have more retries left
				if (i < quorumRetryCount - 1)
				{
					var sleepMs = ThreadSafeRandom.Next(quorumRetryDelayMs);

					Logger.Debug(() => $"Sleeping {sleepMs}ms");

					await TaskUtils.Delay(sleepMs).ConfigureAwait(false);
				}
			}

			// give up
			Logger.Debug(() => $"Could not get lock for id {LockId}, giving up");

			return false;
		}

		private void StartAutoExtendTimer()
		{
			Logger.Debug(() => "Starting auto extend timer");

			lockKeepaliveTimer = new Timer(
				state =>
				{
					try
					{
						Logger.Debug(() => $"Lock renewal timer fired for resource '{Resource}', lockId '{LockId}'");

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
								// Stop the timer
								lockKeepaliveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
							}
						}
					}
					catch (Exception exception)
					{
						// All we can do here is log the exception and swallow it.
						var message = $"Lock renewal timer thread failed for resource '{Resource}', lockId '{LockId}'";
						Logger.ErrorException(message, exception);
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

		private async Task<int> LockAsync()
		{
			var lockTasks = redisCaches.Select(LockInstanceAsync);

			var lockResults = await TaskUtils.WhenAll(lockTasks).ConfigureAwait(false);

			return lockResults.Count(x => x);
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

		private async Task UnlockAsync()
		{
			var unlockTasks = redisCaches.Select(UnlockInstanceAsync);

			await TaskUtils.WhenAll(unlockTasks).ConfigureAwait(false);
		}

		private bool LockInstance(RedisConnection cache)
		{
			var redisKey = GetRedisKey(cache.RedisKeyFormat, Resource);
			var host = GetHost(cache.ConnectionMultiplexer);

			var result = false;

			try
			{
				Logger.Debug(() => $"LockInstance enter {host}: {redisKey}, {LockId}, {expiryTime}");
				result = cache.ConnectionMultiplexer
					.GetDatabase(cache.RedisDatabase)
					.StringSet(redisKey, LockId, expiryTime, When.NotExists, CommandFlags.DemandMaster);
			}
			catch (Exception ex)
			{
				Logger.Debug($"Error locking lock instance {host}: {ex.Message}");
			}

			Logger.Debug(() => $"LockInstance exit {host}: {redisKey}, {LockId}, {result}");

			return result;
		}

		private async Task<bool> LockInstanceAsync(RedisConnection cache)
		{
			var redisKey = GetRedisKey(cache.RedisKeyFormat, Resource);
			var host = GetHost(cache.ConnectionMultiplexer);

			var result = false;

			try
			{
				Logger.Debug(() => $"LockInstanceAsync enter {host}: {redisKey}, {LockId}, {expiryTime}");
				result = await cache.ConnectionMultiplexer
					.GetDatabase(cache.RedisDatabase)
					.StringSetAsync(redisKey, LockId, expiryTime, When.NotExists, CommandFlags.DemandMaster)
					.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logger.Debug($"Error locking lock instance {host}: {ex.Message}");
			}

			Logger.Debug(() => $"LockInstanceAsync exit {host}: {redisKey}, {LockId}, {result}");

			return result;
		}

		private bool ExtendInstance(RedisConnection cache)
		{
			var redisKey = GetRedisKey(cache.RedisKeyFormat, Resource);
			var host = GetHost(cache.ConnectionMultiplexer);

			var result = false;

			try
			{
				Logger.Debug(() => $"ExtendInstance enter {host}: {redisKey}, {LockId}, {expiryTime}");
				var extendResult = (long) cache.ConnectionMultiplexer
					.GetDatabase(cache.RedisDatabase)
					.ScriptEvaluate(ExtendIfMatchingValueScript, new RedisKey[] {redisKey}, new RedisValue[] {LockId, (long) expiryTime.TotalMilliseconds}, CommandFlags.DemandMaster);

				result = (extendResult == 1);
			}
			catch (Exception ex)
			{
				Logger.Debug($"Error extending lock instance {host}: {ex.Message}");
			}

			Logger.Debug(() => $"ExtendInstance exit {host}: {redisKey}, {LockId}, {result}");

			return result;
		}

		private bool UnlockInstance(RedisConnection cache)
		{
			var redisKey = GetRedisKey(cache.RedisKeyFormat, Resource);
			var host = GetHost(cache.ConnectionMultiplexer);

			var result = false;

			try
			{
				Logger.Debug(() => $"UnlockInstance enter {host}: {redisKey}, {LockId}");
				result = (bool) cache.ConnectionMultiplexer
					.GetDatabase(cache.RedisDatabase)
					.ScriptEvaluate(UnlockScript, new RedisKey[] {redisKey}, new RedisValue[] {LockId}, CommandFlags.DemandMaster);
			}
			catch (Exception ex)
			{
				Logger.Debug($"Error unlocking lock instance {host}: {ex.Message}");
			}

			Logger.Debug(() => $"UnlockInstance exit {host}: {redisKey}, {LockId}, {result}");

			return result;
		}

		private async Task<bool> UnlockInstanceAsync(RedisConnection cache)
		{
			var redisKey = GetRedisKey(cache.RedisKeyFormat, Resource);
			var host = GetHost(cache.ConnectionMultiplexer);

			var result = false;

			try
			{
				Logger.Debug(() => $"UnlockInstanceAsync enter {host}: {redisKey}, {LockId}");
				result = (bool) await cache.ConnectionMultiplexer
					.GetDatabase(cache.RedisDatabase)
					.ScriptEvaluateAsync(UnlockScript, new RedisKey[] { redisKey }, new RedisValue[] { LockId }, CommandFlags.DemandMaster)
					.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logger.Debug($"Error unlocking lock instance {host}: {ex.Message}");
			}

			Logger.Debug(() => $"UnlockInstanceAsync exit {host}: {redisKey}, {LockId}, {result}");

			return result;
		}

		private static string GetRedisKey(string redisKeyFormat, string resource)
		{
			return string.Format(redisKeyFormat, resource);
		}

		internal static string GetHost(ConnectionMultiplexer cache)
		{
			var endPoint = cache.GetEndPoints()[0];

			var dnsEndPoint = endPoint as DnsEndPoint;

			if (dnsEndPoint != null)
			{
				return $"{dnsEndPoint.Host}:{dnsEndPoint.Port}";
			}

			var ipEndPoint = endPoint as IPEndPoint;

			if (ipEndPoint != null)
			{
				return $"{ipEndPoint.Address}:{ipEndPoint.Port}";
			}

			return endPoint.ToString();
		}

		public void Dispose()
		{
			Dispose(true);
		}

		protected virtual void Dispose(bool disposing)
		{
			Logger.Debug(() => $"Disposing {Resource} {LockId}");

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

			Logger.Debug("Stopping auto extend timer");

			lockKeepaliveTimer.Change(Timeout.Infinite, Timeout.Infinite);
		}
	}
}
