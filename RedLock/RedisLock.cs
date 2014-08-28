using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using RedLock.Logging;
using StackExchange.Redis;

namespace RedLock
{
	public class RedisLock : IDisposable
	{
		private static readonly Random Rand = new Random();

		private readonly ICollection<ConnectionMultiplexer> redisCaches;
		private readonly Func<string, string> redisKeyFormatter;
		private readonly IRedLockLogger logger;

		private readonly int quorum;
		private readonly int retryCount;
		private readonly int retryDelayMs;
		private readonly double clockDriftFactor;

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

		public RedisLock(
			ICollection<ConnectionMultiplexer> redisCaches,
			string resource,
			TimeSpan expiryTime,
			Func<string, string> redisKeyFormatter = null,
			IRedLockLogger logger = null)
		{
			this.redisCaches = redisCaches;
			this.redisKeyFormatter = redisKeyFormatter ?? DefaultRedisKeyFormatter; 
			this.logger = logger ?? new NullLogger();

			quorum = redisCaches.Count() / 2 + 1;
			retryCount = 3;
			retryDelayMs = 400;
			clockDriftFactor = 0.01;

			Start(resource, expiryTime);
		}

		private void Start(string resource, TimeSpan expiryTime)
		{
			var lockId = Guid.NewGuid().ToString();

			for (var i = 0; i < retryCount; i++)
			{
				logger.DebugWrite("Lock attempt {0} of {1}: {2}, {3}", i + 1, retryCount, resource, expiryTime);

				var startTick = DateTime.UtcNow.Ticks;

				var sw = Stopwatch.StartNew();
				var locksAcquired = Lock(resource, lockId, expiryTime);
				sw.Stop();
				logger.DebugWrite("Locking took {0}ms", sw.ElapsedMilliseconds);

				// Add 2 milliseconds to the drift to account for Redis expires precision,
				// which is 1 milliescond, plus 1 millisecond min drift for small TTLs.
				var driftTicks = ((long) (expiryTime.Ticks*clockDriftFactor)) + TimeSpan.FromMilliseconds(2).Ticks;
				var validityTicks = expiryTime.Ticks - ((DateTime.UtcNow.Ticks) - startTick) - driftTicks;

				logger.DebugWrite("Acquired locks for id {0} in {1} of {2} instances, quorum is {3}, validityTicks is {4}", lockId, locksAcquired, redisCaches.Count(), quorum, validityTicks);

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
				
				Unlock(redisKeyFormatter(resource), lockId);

				// only sleep if we have more retries left
				if (i < retryCount - 1)
				{
					var sleepMs = Rand.Next(retryDelayMs);

					logger.DebugWrite("Sleeping {0}ms", sleepMs);

					Thread.Sleep(sleepMs);
				}
			}

			logger.DebugWrite("Could not get lock for id {0}, giving up", lockId);
		}

		private int Lock(string resource, string lockId, TimeSpan expiryTime)
		{
			var locksAcquired = 0;

			foreach (var cache in redisCaches)
			{
				if (LockInstance(cache, redisKeyFormatter(resource), lockId, expiryTime))
				{
					locksAcquired++;
				}
			}
			
			return locksAcquired;
		}

		private int LockParallel(string resource, string lockId, TimeSpan expiryTime)
		{
			var locksAcquired = 0;

			Parallel.ForEach(redisCaches, cache =>
			{
				if (LockInstance(cache, redisKeyFormatter(resource), lockId, expiryTime))
				{
					Interlocked.Increment(ref locksAcquired);
				}
			});

			return locksAcquired;
		}

		private void Unlock()
		{
			if (LockInfo == null)
			{
				return;
			}

			Unlock(redisKeyFormatter(LockInfo.Resource), LockInfo.LockId);
		}

		private void Unlock(string redisKey, string lockId)
		{
			foreach (var cache in redisCaches)
			{
				UnlockInstance(cache, redisKey, lockId);
			}

			LockInfo = null;
		}

		private void UnlockParallel(string redisKey, string lockId)
		{
			Parallel.ForEach(redisCaches, cache => UnlockInstance(cache, redisKey, lockId));
		}

		private bool LockInstance(ConnectionMultiplexer cache, string redisKey, string lockId, TimeSpan expiryTime)
		{
			var host = GetHost(cache);

			var result = false;

			try
			{
				logger.DebugWrite("LockInstance enter {0}: {1}, {2}, {3}", host, redisKey, lockId, expiryTime);
				result = cache.GetDatabase().StringSet(redisKey, lockId, expiryTime, When.NotExists, CommandFlags.DemandMaster);
			}
			catch (Exception ex)
			{
				logger.DebugWrite("Error locking lock instance {0}: {1}", host, ex.Message);
			}

			logger.DebugWrite("LockInstance exit {0}: {1}, {2}, {3}", host, redisKey, lockId, result);

			return result;
		}

		private bool UnlockInstance(ConnectionMultiplexer cache, string redisKey, string lockId)
		{
			var host = GetHost(cache);

			var result = false;

			try
			{
				logger.DebugWrite("UnlockInstance enter {0}: {1}, {2}", host, redisKey, lockId);
				result = (bool)cache.GetDatabase()
					.ScriptEvaluate(UnlockScript, new RedisKey[] {redisKey}, new RedisValue[] {lockId}, CommandFlags.DemandMaster);
			}
			catch (Exception ex)
			{
				logger.DebugWrite("Error unlocking lock instance {0}: {1}", host, ex.Message);
			}

			logger.DebugWrite("UnlockInstance exit {0}: {1}, {2}, {3}", host, redisKey, lockId, result);

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
			logger.DebugWrite("Disposing");

			Unlock();
		}
	}
}
