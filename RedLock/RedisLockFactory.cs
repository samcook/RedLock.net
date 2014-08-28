using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using RedLock.Logging;
using StackExchange.Redis;

namespace RedLock
{
	public class RedisLockFactory : IDisposable
	{
		private readonly IList<ConnectionMultiplexer> redisCaches;
		private readonly IRedLockLogger logger;

		public RedisLockFactory(params EndPoint[] redisEndPoints)
			: this(redisEndPoints, null)
		{
		}

		public RedisLockFactory(IEnumerable<EndPoint> redisEndPoints)
			: this(redisEndPoints, null)
		{
		}

		public RedisLockFactory(IEnumerable<EndPoint> redisEndPoints, IRedLockLogger logger)
		{
			redisCaches = CreateRedisCaches(redisEndPoints.ToArray());
			this.logger = logger ?? new NullLogger();
		}

		private IList<ConnectionMultiplexer> CreateRedisCaches(ICollection<EndPoint> redisEndPoints)
		{
			var caches = new List<ConnectionMultiplexer>(redisEndPoints.Count);

			foreach (var endPoint in redisEndPoints)
			{
				var configuration = new ConfigurationOptions
				{
					AbortOnConnectFail = false,
					ConnectTimeout = 100
				};

				configuration.EndPoints.Add(endPoint);

				caches.Add(ConnectionMultiplexer.Connect(configuration));
			}

			return caches;
		}

		/// <summary>
		/// Gets a RedisLock using the factory's set of redis endpoints. You should check the IsAcquired property before performing actions.
		/// </summary>
		/// <param name="resource">The resource string to lock on. Only one RedisLock should be acquired for any given resource at once.</param>
		/// <param name="expiryTime">How long the lock should be held for.
		/// RedisLocks will automatically extend if the process that created the RedisLock is still alive and the RedisLock hasn't been disposed.</param>
		/// <returns>A RedisLock object.</returns>
		public RedisLock Create(string resource, TimeSpan expiryTime)
		{
			return new RedisLock(redisCaches, resource, expiryTime, logger: logger);
		}

		/// <summary>
		/// Gets a RedisLock using the factory's set of redis endpoints. You should check the IsAcquired property before performing actions.
		/// Blocks and retries up to the specified time limits.
		/// </summary>
		/// <param name="resource">The resource string to lock on. Only one RedisLock should be acquired for any given resource at once.</param>
		/// <param name="expiryTime">How long the lock should be held for.
		/// RedisLocks will automatically extend if the process that created the RedisLock is still alive and the RedisLock hasn't been disposed.</param>
		/// <param name="waitTime">How long to block for until a lock can be acquired.</param>
		/// <param name="retryTime">How long to wait between retries when trying to acquire a lock.</param>
		/// <returns>A RedisLock object.</returns>
		public RedisLock Create(string resource, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime)
		{
			return new RedisLock(redisCaches, resource, expiryTime, waitTime, retryTime, logger: logger);
		}

		public void Dispose()
		{
			foreach (var cache in redisCaches)
			{
				cache.Dispose();
			}
		}
	}
}