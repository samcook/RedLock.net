using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RedLockNet.SERedis.Internal;
using RedLockNet.SERedis.Util;
using StackExchange.Redis;

namespace RedLockNet.SERedis
{
	public class RedLock : IRedLock
	{
		private readonly object lockObject = new object();

		private readonly ICollection<RedisConnection> redisCaches;
		private readonly ILogger<RedLock> logger;

		private readonly int quorum;
		private readonly int quorumRetryCount;
		private readonly int quorumRetryDelayMs;
		private readonly double clockDriftFactor;
		private bool isDisposed;

		private Timer lockKeepaliveTimer;

		private static readonly string UnlockScript = EmbeddedResourceLoader.GetEmbeddedResource("RedLockNet.SERedis.Lua.Unlock.lua");

		// Set the expiry for the given key if its value matches the supplied value.
		// Returns 1 on success, 0 on failure setting expiry or key not existing, -1 if the key value didn't match
		private static readonly string ExtendIfMatchingValueScript = EmbeddedResourceLoader.GetEmbeddedResource("RedLockNet.SERedis.Lua.Extend.lua");

		public string Resource { get; }
		public string LockId { get; }
		public bool IsAcquired => Status == RedLockStatus.Acquired;
		public RedLockStatus Status { get; private set; }
		public RedLockInstanceSummary InstanceSummary { get; private set; }
		public int ExtendCount { get; private set; }

		private readonly TimeSpan expiryTime;
		private readonly TimeSpan? waitTime;
		private readonly TimeSpan? retryTime;
		private CancellationToken cancellationToken;

		private readonly TimeSpan minimumExpiryTime = TimeSpan.FromMilliseconds(10);
		private readonly TimeSpan minimumRetryTime = TimeSpan.FromMilliseconds(10);

		private RedLock(
			ILogger<RedLock> logger,
			ICollection<RedisConnection> redisCaches,
			string resource,
			TimeSpan expiryTime,
			TimeSpan? waitTime = null,
			TimeSpan? retryTime = null,
			CancellationToken? cancellationToken = null)
		{
			this.logger = logger;

			if (expiryTime < minimumExpiryTime)
			{
				logger.LogWarning($"Expiry time {expiryTime.TotalMilliseconds}ms too low, setting to {minimumExpiryTime.TotalMilliseconds}ms");
				expiryTime = minimumExpiryTime;
			}

			if (retryTime != null && retryTime.Value < minimumRetryTime)
			{
				logger.LogWarning($"Retry time {retryTime.Value.TotalMilliseconds}ms too low, setting to {minimumRetryTime.TotalMilliseconds}ms");
				retryTime = minimumRetryTime;
			}

			this.redisCaches = redisCaches;

			quorum = redisCaches.Count / 2 + 1;
			quorumRetryCount = 3;
			quorumRetryDelayMs = 400;
			clockDriftFactor = 0.01;

			Resource = resource;
			LockId = Guid.NewGuid().ToString();
			this.expiryTime = expiryTime;
			this.waitTime = waitTime;
			this.retryTime = retryTime;
			this.cancellationToken = cancellationToken ?? CancellationToken.None;
		}

		internal static RedLock Create(
			ILogger<RedLock> logger,
			ICollection<RedisConnection> redisCaches,
			string resource,
			TimeSpan expiryTime,
			TimeSpan? waitTime = null,
			TimeSpan? retryTime = null,
			CancellationToken? cancellationToken = null)
		{
			var redisLock = new RedLock(
				logger,
				redisCaches,
				resource,
				expiryTime,
				waitTime,
				retryTime,
				cancellationToken);

			redisLock.Start();
			
			return redisLock;
		}

		internal static async Task<RedLock> CreateAsync(
			ILogger<RedLock> logger,
			ICollection<RedisConnection> redisCaches,
			string resource,
			TimeSpan expiryTime,
			TimeSpan? waitTime = null,
			TimeSpan? retryTime = null,
			CancellationToken? cancellationToken = null)
		{
			var redisLock = new RedLock(
				logger,
				redisCaches,
				resource,
				expiryTime,
				waitTime,
				retryTime,
				cancellationToken);

			await redisLock.StartAsync().ConfigureAwait(false);
			
			return redisLock;
		}

		private void Start()
		{
			if (waitTime.HasValue && retryTime.HasValue && waitTime.Value.TotalMilliseconds > 0 && retryTime.Value.TotalMilliseconds > 0)
			{
				var stopwatch = Stopwatch.StartNew();

				// ReSharper disable PossibleInvalidOperationException
				while (!IsAcquired && stopwatch.Elapsed <= waitTime.Value)
				{
					(Status, InstanceSummary) = Acquire();

					if (!IsAcquired)
					{
						TaskUtils.Delay(retryTime.Value, cancellationToken).Wait(cancellationToken);
					}
				}
				// ReSharper restore PossibleInvalidOperationException
			}
			else
			{
				(Status, InstanceSummary) = Acquire();
			}

			logger.LogInformation($"Lock status: {Status} ({InstanceSummary}), {Resource} ({LockId})");

			if (IsAcquired)
			{
				StartAutoExtendTimer();
			}
		}

		private async Task StartAsync()
		{
			if (waitTime.HasValue && retryTime.HasValue && waitTime.Value.TotalMilliseconds > 0 && retryTime.Value.TotalMilliseconds > 0)
			{
				var stopwatch = Stopwatch.StartNew();

				// ReSharper disable PossibleInvalidOperationException
				while (!IsAcquired && stopwatch.Elapsed <= waitTime.Value)
				{
					(Status, InstanceSummary) = await AcquireAsync().ConfigureAwait(false);

					if (!IsAcquired)
					{
						await TaskUtils.Delay(retryTime.Value, cancellationToken).ConfigureAwait(false);
					}
				}
				// ReSharper restore PossibleInvalidOperationException
			}
			else
			{
				(Status, InstanceSummary) = await AcquireAsync().ConfigureAwait(false);
			}

			logger.LogInformation($"Lock status: {Status} ({InstanceSummary}), {Resource} ({LockId})");

			if (IsAcquired)
			{
				StartAutoExtendTimer();
			}
		}

		private (RedLockStatus, RedLockInstanceSummary) Acquire()
		{
			var lockSummary = new RedLockInstanceSummary();

			for (var i = 0; i < quorumRetryCount; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var iteration = i + 1;
				logger.LogDebug($"Lock attempt {iteration}/{quorumRetryCount}: {Resource} ({LockId}), expiry: {expiryTime}");

				var stopwatch = Stopwatch.StartNew();

				lockSummary = Lock();

				var validityTicks = GetRemainingValidityTicks(stopwatch);

				logger.LogDebug($"Acquired locks for {Resource} ({LockId}) in {lockSummary.Acquired}/{redisCaches.Count} instances, quorum: {quorum}, validityTicks: {validityTicks}");

				if (lockSummary.Acquired >= quorum && validityTicks > 0)
				{
					return (RedLockStatus.Acquired, lockSummary);
				}
				
				// we failed to get enough locks for a quorum, unlock everything and try again
				Unlock();

				// only sleep if we have more retries left
				if (i < quorumRetryCount - 1)
				{
					var sleepMs = ThreadSafeRandom.Next(quorumRetryDelayMs);

					logger.LogDebug($"Sleeping {sleepMs}ms");

					TaskUtils.Delay(sleepMs, cancellationToken).Wait(cancellationToken);
				}
			}

			var status = GetFailedRedLockStatus(lockSummary);

			// give up
			logger.LogDebug($"Could not acquire quorum after {quorumRetryCount} attempts, giving up: {Resource} ({LockId}). {lockSummary}.");

			return (status, lockSummary);
		}

		private async Task<(RedLockStatus, RedLockInstanceSummary)> AcquireAsync()
		{
			var lockSummary = new RedLockInstanceSummary();

			for (var i = 0; i < quorumRetryCount; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var iteration = i + 1;
				logger.LogDebug($"Lock attempt {iteration}/{quorumRetryCount}: {Resource} ({LockId}), expiry: {expiryTime}");

				var stopwatch = Stopwatch.StartNew();

				lockSummary = await LockAsync().ConfigureAwait(false);

				var validityTicks = GetRemainingValidityTicks(stopwatch);

				logger.LogDebug($"Acquired locks for {Resource} ({LockId}) in {lockSummary.Acquired}/{redisCaches.Count} instances, quorum: {quorum}, validityTicks: {validityTicks}");

				if (lockSummary.Acquired >= quorum && validityTicks > 0)
				{
					return (RedLockStatus.Acquired, lockSummary);
				}

				// we failed to get enough locks for a quorum, unlock everything and try again
				await UnlockAsync().ConfigureAwait(false);

				// only sleep if we have more retries left
				if (i < quorumRetryCount - 1)
				{
					var sleepMs = ThreadSafeRandom.Next(quorumRetryDelayMs);

					logger.LogDebug($"Sleeping {sleepMs}ms");

					await TaskUtils.Delay(sleepMs, cancellationToken).ConfigureAwait(false);
				}
			}

			var status = GetFailedRedLockStatus(lockSummary);

			// give up
			logger.LogDebug($"Could not acquire quorum after {quorumRetryCount} attempts, giving up: {Resource} ({LockId}). {lockSummary}.");

			return (status, lockSummary);
		}

		private void StartAutoExtendTimer()
		{
			var interval = expiryTime.TotalMilliseconds / 2;

			logger.LogDebug($"Starting auto extend timer with {interval}ms interval");
            var stopwatch = Stopwatch.StartNew();

            lockKeepaliveTimer = new Timer(
				state =>
				{
					try
					{
						logger.LogTrace($"Lock renewal timer fired: {Resource} ({LockId})");

                        var extendSummary = Extend();

                        var validityTicks = GetRemainingValidityTicks(stopwatch);

						if (extendSummary.Acquired >= quorum && validityTicks > 0)
						{
                                Status = RedLockStatus.Acquired;
                                InstanceSummary = extendSummary;
                                ExtendCount++;

                                logger.LogDebug($"Extended lock, {Status} ({InstanceSummary}): {Resource} ({LockId})");
						}
                        else
                        {
                            Status = GetFailedRedLockStatus(extendSummary);
                            InstanceSummary = extendSummary;

							logger.LogWarning($"Failed to extend lock, {Status} ({InstanceSummary}): {Resource} ({LockId})");
						}
					}
					catch (Exception exception)
					{
						// All we can do here is log the exception and swallow it.
						var message = $"Lock renewal timer thread failed: {Resource} ({LockId})";
						logger.LogError(null, exception, message);
					}
				},
				null,
				(int) interval,
				(int) interval);
		}
        
		private long GetRemainingValidityTicks(Stopwatch sw)
		{
            // Add 2 milliseconds to the drift to account for Redis expires precision,
            // which is 1 milliescond, plus 1 millisecond min drift for small TTLs.
            var driftTicks = (long)(expiryTime.Ticks * clockDriftFactor) + TimeSpan.FromMilliseconds(2).Ticks;
            var validityTicks = expiryTime.Ticks - sw.Elapsed.Ticks - driftTicks;
            return validityTicks;
        }

		private RedLockInstanceSummary Lock()
		{
			var lockResults = new ConcurrentBag<RedLockInstanceResult>();

			Parallel.ForEach(redisCaches, cache =>
			{
				lockResults.Add(LockInstance(cache));
			});

			return PopulateRedLockResult(lockResults);
		}

		private async Task<RedLockInstanceSummary> LockAsync()
		{
			var lockTasks = redisCaches.Select(LockInstanceAsync);

			var lockResults = await TaskUtils.WhenAll(lockTasks).ConfigureAwait(false);

			return PopulateRedLockResult(lockResults);
		}

		private RedLockInstanceSummary Extend()
		{
			var extendResults = new ConcurrentBag<RedLockInstanceResult>();

			Parallel.ForEach(redisCaches, cache =>
			{
				extendResults.Add(ExtendInstance(cache));
			});

			return PopulateRedLockResult(extendResults);
		}

		private void Unlock()
		{
			Parallel.ForEach(redisCaches, UnlockInstance);
		}

		private async Task UnlockAsync()
		{
			var unlockTasks = redisCaches.Select(UnlockInstanceAsync);

			await TaskUtils.WhenAll(unlockTasks).ConfigureAwait(false);
		}

		private RedLockInstanceResult LockInstance(RedisConnection cache)
		{
			var redisKey = GetRedisKey(cache.RedisKeyFormat, Resource);
			var host = GetHost(cache.ConnectionMultiplexer);

			RedLockInstanceResult result;

			try
			{
				logger.LogTrace($"LockInstance enter {host}: {redisKey}, {LockId}, {expiryTime}");
				var redisResult = cache.ConnectionMultiplexer
					.GetDatabase(cache.RedisDatabase)
					.StringSet(redisKey, LockId, expiryTime, When.NotExists, CommandFlags.DemandMaster);

				result = redisResult ? RedLockInstanceResult.Success : RedLockInstanceResult.Conflicted;
			}
			catch (Exception ex)
			{
				logger.LogDebug($"Error locking lock instance {host}: {ex.Message}");

				result = RedLockInstanceResult.Error;
			}

			logger.LogTrace($"LockInstance exit {host}: {redisKey}, {LockId}, {result}");

			return result;
		}

		private async Task<RedLockInstanceResult> LockInstanceAsync(RedisConnection cache)
		{
			var redisKey = GetRedisKey(cache.RedisKeyFormat, Resource);
			var host = GetHost(cache.ConnectionMultiplexer);

			RedLockInstanceResult result;

			try
			{
				logger.LogTrace($"LockInstanceAsync enter {host}: {redisKey}, {LockId}, {expiryTime}");
				var redisResult = await cache.ConnectionMultiplexer
					.GetDatabase(cache.RedisDatabase)
					.StringSetAsync(redisKey, LockId, expiryTime, When.NotExists, CommandFlags.DemandMaster)
					.ConfigureAwait(false);

				result = redisResult ? RedLockInstanceResult.Success : RedLockInstanceResult.Conflicted;
			}
			catch (Exception ex)
			{
				logger.LogDebug($"Error locking lock instance {host}: {ex.Message}");

				result = RedLockInstanceResult.Error;
			}

			logger.LogTrace($"LockInstanceAsync exit {host}: {redisKey}, {LockId}, {result}");

			return result;
		}

		private RedLockInstanceResult ExtendInstance(RedisConnection cache)
		{
			var redisKey = GetRedisKey(cache.RedisKeyFormat, Resource);
			var host = GetHost(cache.ConnectionMultiplexer);

			RedLockInstanceResult result;

			try
			{
				logger.LogTrace($"ExtendInstance enter {host}: {redisKey}, {LockId}, {expiryTime}");
				
				// Returns 1 on success, 0 on failure setting expiry or key not existing, -1 if the key value didn't match
				var extendResult = (long) cache.ConnectionMultiplexer
					.GetDatabase(cache.RedisDatabase)
					.ScriptEvaluate(ExtendIfMatchingValueScript, new RedisKey[] {redisKey}, new RedisValue[] {LockId, (long) expiryTime.TotalMilliseconds}, CommandFlags.DemandMaster);

				result = extendResult == 1 ? RedLockInstanceResult.Success
					: extendResult == -1 ? RedLockInstanceResult.Conflicted
					: RedLockInstanceResult.Error;
			}
			catch (Exception ex)
			{
				logger.LogDebug($"Error extending lock instance {host}: {ex.Message}");

				result = RedLockInstanceResult.Error;
			}

			logger.LogTrace($"ExtendInstance exit {host}: {redisKey}, {LockId}, {result}");

			return result;
		}

		private void UnlockInstance(RedisConnection cache)
		{
			var redisKey = GetRedisKey(cache.RedisKeyFormat, Resource);
			var host = GetHost(cache.ConnectionMultiplexer);

			var result = false;

			try
			{
				logger.LogTrace($"UnlockInstance enter {host}: {redisKey}, {LockId}");
				result = (bool) cache.ConnectionMultiplexer
					.GetDatabase(cache.RedisDatabase)
					.ScriptEvaluate(UnlockScript, new RedisKey[] {redisKey}, new RedisValue[] {LockId}, CommandFlags.DemandMaster);
			}
			catch (Exception ex)
			{
				logger.LogDebug($"Error unlocking lock instance {host}: {ex.Message}");
			}

			logger.LogTrace($"UnlockInstance exit {host}: {redisKey}, {LockId}, {result}");
		}

		private async Task<bool> UnlockInstanceAsync(RedisConnection cache)
		{
			var redisKey = GetRedisKey(cache.RedisKeyFormat, Resource);
			var host = GetHost(cache.ConnectionMultiplexer);

			var result = false;

			try
			{
				logger.LogTrace($"UnlockInstanceAsync enter {host}: {redisKey}, {LockId}");
				result = (bool) await cache.ConnectionMultiplexer
					.GetDatabase(cache.RedisDatabase)
					.ScriptEvaluateAsync(UnlockScript, new RedisKey[] { redisKey }, new RedisValue[] { LockId }, CommandFlags.DemandMaster)
					.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				logger.LogDebug($"Error unlocking lock instance {host}: {ex.Message}");
			}

			logger.LogTrace($"UnlockInstanceAsync exit {host}: {redisKey}, {LockId}, {result}");

			return result;
		}

		private static string GetRedisKey(string redisKeyFormat, string resource)
		{
			return string.Format(redisKeyFormat, resource);
		}

		internal static string GetHost(IConnectionMultiplexer cache)
		{
			var result = new StringBuilder();

			foreach (var endPoint in cache.GetEndPoints())
			{
				var server = cache.GetServer(endPoint);

				result.Append(server.EndPoint.GetFriendlyName());
				result.Append(" (");
				result.Append(server.IsSlave ? "slave" : "master");
				result.Append(server.IsConnected ? "" : ", disconnected");
				result.Append("), ");
			}

			return result.ToString().TrimEnd(' ', ',');
		}

		public void Dispose()
		{
			Dispose(true);
		}

		protected virtual void Dispose(bool disposing)
		{
			logger.LogDebug($"Disposing {Resource} ({LockId})");

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

			Status = RedLockStatus.Unlocked;
			InstanceSummary = new RedLockInstanceSummary();

			isDisposed = true;
		}

		private RedLockStatus GetFailedRedLockStatus(RedLockInstanceSummary lockResult)
		{
			if (lockResult.Acquired >= quorum)
			{
				// if we got here with a quorum then validity must have expired
				return RedLockStatus.Expired;
			}

			if (lockResult.Acquired + lockResult.Conflicted >= quorum)
			{
				// we had enough instances for a quorum, but some were locked with another LockId
				return RedLockStatus.Conflicted;
			}

			return RedLockStatus.NoQuorum;
		}

		private static RedLockInstanceSummary PopulateRedLockResult(IEnumerable<RedLockInstanceResult> instanceResults)
		{
			var acquired = 0;
			var conflicted = 0;
			var error = 0;

			foreach (var instanceResult in instanceResults)
			{
				switch (instanceResult)
				{
					case RedLockInstanceResult.Success:
						acquired++;
						break;
					case RedLockInstanceResult.Conflicted:
						conflicted++;
						break;
					case RedLockInstanceResult.Error:
						error++;
						break;
				}
			}

			return new RedLockInstanceSummary(acquired, conflicted, error);
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

			logger.LogDebug("Stopping auto extend timer");

			lockKeepaliveTimer.Change(Timeout.Infinite, Timeout.Infinite);
		}
	}
}
