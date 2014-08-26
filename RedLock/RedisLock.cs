using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using log4net;
using StackExchange.Redis;

namespace RedLock
{
	public class RedisLock : IDisposable
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (RedisLock));
		private static readonly Random Rand = new Random();

		private readonly ICollection<ConnectionMultiplexer> redisCaches;
		private readonly int quorum;
		private readonly int retryCount;
		private readonly int retryDelayMs;
		private readonly double clockDriftFactor;
		private readonly Func<string, string> redisKeyFormatter;

		private const string UnlockScript =
			@"if redis.call('get', KEYS[1]) == ARGV[1] then
				return redis.call('del', KEYS[1])
			else
				return 0
			end";

		public RedLockInfo LockInfo { get; private set; }

		public bool IsAcquired
		{
			get { return LockInfo != null; }
		}

		public Func<string, string> DefaultRedisKeyFormatter
		{
			get { return s => string.Format("redlock-{0}", s); }
		}

		public RedisLock(ICollection<ConnectionMultiplexer> redisCaches, string resource, TimeSpan expiryTime, Func<string, string> redisKeyFormatter = null)
		{
			this.redisCaches = redisCaches;
			quorum = redisCaches.Count() / 2 + 1;
			retryCount = 3;
			retryDelayMs = 200;
			clockDriftFactor = 0.01;
			this.redisKeyFormatter = redisKeyFormatter ?? DefaultRedisKeyFormatter;

			Lock(resource, expiryTime);
		}

		private void Lock(string resource, TimeSpan expiryTime)
		{
			var lockId = Guid.NewGuid().ToString();

			for (var i = 0; i < retryCount; i++)
			{
				Log.DebugFormat("Lock attempt {0} of {1}: {2}, {3}", i + 1, retryCount, resource, expiryTime);

				var startTick = DateTime.UtcNow.Ticks;

				//var lockTasks = new List<Task<bool>>(redisCaches.Count());
				var locksAcquired = 0;

				foreach (var cache in redisCaches)
				{
					//lockTasks.Add(LockInstance(cache, resource, lockId, ttl));
					if (LockInstance(cache, redisKeyFormatter(resource), lockId, expiryTime))
					{
						locksAcquired++;
					}
				}

				//var lockResults = Task.WhenAll(lockTasks);

				// Add 2 milliseconds to the drift to account for Redis expires precision,
				// which is 1 milliescond, plus 1 millisecond min drift for small TTLs.
				var driftTicks = ((long) (expiryTime.Ticks*clockDriftFactor)) + TimeSpan.FromMilliseconds(2).Ticks;
				var validityTicks = expiryTime.Ticks - ((DateTime.UtcNow.Ticks) - startTick) - driftTicks;

				//var locksAcquired = lockResults.Count(x => x);

				Log.DebugFormat("Acquired locks for id {0} in {1} of {2} instances, quorum is {3}, validityTicks is {4}", lockId, locksAcquired, redisCaches.Count(), quorum, validityTicks);

				if (locksAcquired >= quorum && validityTicks > 0)
				{
					LockInfo = new RedLockInfo
					{
						Validity = TimeSpan.FromTicks(validityTicks),
						Resource = resource,
						LockId = lockId
					};

					return;
				}
				
				Unlock();

				//var unlockTasks = new List<Task>(redisCaches.Count());
				foreach (var cache in redisCaches)
				{
					//unlockTasks.Add(UnlockInstance(cache, resource, lockId));
					UnlockInstance(cache, resource, lockId);
				}

				//Task.WaitAll(unlockTasks.ToArray());

				// only sleep if we have more retries left
				if (i < retryCount - 1)
				{
					var sleepMs = Rand.Next(retryDelayMs);

					Log.DebugFormat("Sleeping {0}ms", sleepMs);

					Thread.Sleep(sleepMs);
				}
			}

			Log.DebugFormat("Could not get lock for id {0}, giving up", lockId);
		}

		private void Unlock()
		{
			if (LockInfo == null)
			{
				return;
			}

			//var unlockTasks = new List<Task>(redisCaches.Count());

			foreach (var cache in redisCaches)
			{
				//unlockTasks.Add(UnlockInstance(cache, lockInfo.Resource, lockInfo.LockId));
				UnlockInstance(cache, redisKeyFormatter(LockInfo.Resource), LockInfo.LockId);
			}

			//Task.WaitAll(unlockTasks.ToArray());
			LockInfo = null;
		}

		private static bool LockInstance(ConnectionMultiplexer cache, string redisKey, string lockId, TimeSpan expiryTime)
		{
			var port = ((DnsEndPoint) cache.GetEndPoints()[0]).Port;

			var result = false;

			try
			{
				Log.DebugFormat("LockInstance enter: {0}, {1}, {2}, {3}", port, redisKey, lockId, expiryTime);
				result = cache.GetDatabase().StringSet(redisKey, lockId, expiryTime, When.NotExists, CommandFlags.DemandMaster);
			}
			catch (Exception ex)
			{
				Log.DebugFormat("Error locking lock instance: {0}", ex.Message);
			}
		
			Log.DebugFormat("LockInstance exit: {0}, {1}, {2}, {3}", port, redisKey, lockId, result);

			return result;
		}

		private static void UnlockInstance(ConnectionMultiplexer cache, string redisKey, string lockId)
		{
			try
			{
				//Log.DebugFormat("UnlockInstance enter: {0}, {1}", lockKey, lockId);
				cache.GetDatabase().ScriptEvaluate(UnlockScript, new RedisKey[] {redisKey}, new RedisValue[] {lockId}, CommandFlags.DemandMaster);
			}
			catch (Exception ex)
			{
				Log.DebugFormat("Error unlocking lock instance: {0}", ex.Message);
			}
		
			//Log.DebugFormat("UnlockInstance exit: {0}, {1}", lockKey, lockId);
		}

		public void Dispose()
		{
			Log.Debug("Disposing");

			Unlock();
		}
	}
}
