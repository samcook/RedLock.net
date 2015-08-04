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
		private const int DefaultConnectionTimeout = 100;
		private const int DefaultRedisDatabase = 0;
		private readonly IList<RedisConnection> redisCaches;
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
			var endPoints = redisEndPoints.Select(endPoint => new RedisLockEndPoint
			{
				EndPoint = endPoint
			});

			redisCaches = CreateRedisCaches(endPoints.ToArray());
			this.logger = logger ?? new NullLogger();
		}

		public RedisLockFactory(params RedisLockEndPoint[] redisEndPoints)
			: this(redisEndPoints, null)
		{
		}

		public RedisLockFactory(IEnumerable<RedisLockEndPoint> redisEndPoints)
			: this(redisEndPoints, null)
		{
		}

		public RedisLockFactory(IEnumerable<RedisLockEndPoint> redisEndPoints, IRedLockLogger logger)
		{
			redisCaches = CreateRedisCaches(redisEndPoints.ToArray());
			this.logger = logger ?? new NullLogger();
		}

		private static IList<RedisConnection> CreateRedisCaches(ICollection<RedisLockEndPoint> redisEndPoints)
		{
			var caches = new List<RedisConnection>(redisEndPoints.Count);

			foreach (var endPoint in redisEndPoints)
			{
				var configuration = new ConfigurationOptions
				{
					AbortOnConnectFail = false,
					ConnectTimeout = endPoint.ConnectionTimeout ?? DefaultConnectionTimeout,
					Ssl = endPoint.Ssl,
					Password = endPoint.Password
				};

				configuration.EndPoints.Add(endPoint.EndPoint);

				caches.Add(new RedisConnection
				{
					ConnectionMultiplexer = ConnectionMultiplexer.Connect(configuration),
					RedisDatabase = endPoint.RedisDatabase ?? DefaultRedisDatabase,
					RedisKeyFormat = string.IsNullOrEmpty(endPoint.RedisKeyFormat) ? RedisLock.DefaultRedisKeyFormat : endPoint.RedisKeyFormat
				});
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
				cache.ConnectionMultiplexer.Dispose();
			}
		}
	}
}