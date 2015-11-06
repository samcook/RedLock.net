using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace RedLock
{
	public class RedisLockFactory : IDisposable
	{
		private const int DefaultConnectionTimeout = 100;
		private const int DefaultRedisDatabase = 0;
		private readonly IList<RedisConnection> redisCaches;

		public RedisLockFactory(IEnumerable<EndPoint> redisEndPoints)
			: this(redisEndPoints.ToArray())
		{
		}

		public RedisLockFactory(params EndPoint[] redisEndPoints)
		{
			var endPoints = redisEndPoints.Select(endPoint => new RedisLockEndPoint
			{
				EndPoint = endPoint
			});

			redisCaches = CreateRedisCaches(endPoints.ToArray());
		}

		public RedisLockFactory(IEnumerable<RedisLockEndPoint> redisEndPoints)
			: this(redisEndPoints.ToArray())
		{
		}

		public RedisLockFactory(params RedisLockEndPoint[] redisEndPoints)
		{
			redisCaches = CreateRedisCaches(redisEndPoints.ToArray());
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
			return RedisLock.Create(redisCaches, resource, expiryTime);
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
			return RedisLock.Create(redisCaches, resource, expiryTime, waitTime, retryTime);
		}

		/// <summary>
		/// Gets a RedisLock using the factory's set of redis endpoints. You should check the IsAcquired property before performing actions.
		/// </summary>
		/// <param name="resource">The resource string to lock on. Only one RedisLock should be acquired for any given resource at once.</param>
		/// <param name="expiryTime">How long the lock should be held for.
		/// RedisLocks will automatically extend if the process that created the RedisLock is still alive and the RedisLock hasn't been disposed.</param>
		/// <returns>A RedisLock object.</returns>
		public async Task<RedisLock> CreateAsync(string resource, TimeSpan expiryTime)
		{
			return await RedisLock.CreateAsync(redisCaches, resource, expiryTime).ConfigureAwait(false);
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
		public async Task<RedisLock> CreateAsync(string resource, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime)
		{
			return await RedisLock.CreateAsync(redisCaches, resource, expiryTime, waitTime, retryTime);
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