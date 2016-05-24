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
	public class RedisLockFactory : IDisposable
	{
		private static readonly ILog Logger = LogProvider.GetCurrentClassLogger();

		private const int DefaultConnectionTimeout = 100;
		private const int DefaultRedisDatabase = 0;
		private const int DefaultConfigCheckSeconds = 10;
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
			if (!redisEndPoints.Any())
			{
				throw new ArgumentException("No endpoints specified");
			}

			var caches = new List<RedisConnection>(redisEndPoints.Count);

			foreach (var endPoint in redisEndPoints)
			{
				var configuration = new ConfigurationOptions
				{
					AbortOnConnectFail = false,
					ConnectTimeout = endPoint.ConnectionTimeout ?? DefaultConnectionTimeout,
					Ssl = endPoint.Ssl,
					Password = endPoint.Password,
					ConfigCheckSeconds = endPoint.ConfigCheckSeconds ?? DefaultConfigCheckSeconds
				};

				foreach (var e in endPoint.EndPoints)
				{
					configuration.EndPoints.Add(e);
				}

				var redisConnection = new RedisConnection
				{
					ConnectionMultiplexer = ConnectionMultiplexer.Connect(configuration),
					RedisDatabase = endPoint.RedisDatabase ?? DefaultRedisDatabase,
					RedisKeyFormat = string.IsNullOrEmpty(endPoint.RedisKeyFormat) ? RedisLock.DefaultRedisKeyFormat : endPoint.RedisKeyFormat
				};

				redisConnection.ConnectionMultiplexer.ConnectionFailed += (sender, args) =>
				{
					Logger.Debug(() => $"ConnectionFailed: {args.EndPoint.GetFriendlyName()} ConnectionType: {args.ConnectionType} FailureType: {args.FailureType}");
				};

				redisConnection.ConnectionMultiplexer.ConnectionRestored += (sender, args) =>
				{
					Logger.Debug(() => $"ConnectionRestored: {args.EndPoint.GetFriendlyName()} ConnectionType: {args.ConnectionType} FailureType: {args.FailureType}");
				};

				redisConnection.ConnectionMultiplexer.ConfigurationChanged += (sender, args) =>
				{
					Logger.Debug(() => $"ConfigurationChanged: {args.EndPoint.GetFriendlyName()}");
				};

				redisConnection.ConnectionMultiplexer.ConfigurationChangedBroadcast += (sender, args) =>
				{
					Logger.Debug(() => $"ConfigurationChangedBroadcast: {args.EndPoint.GetFriendlyName()}");
				};

				caches.Add(redisConnection);
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
		/// Blocks and retries up to the specified time limits.
		/// </summary>
		/// <param name="resource">The resource string to lock on. Only one RedisLock should be acquired for any given resource at once.</param>
		/// <param name="expiryTime">How long the lock should be held for.
		/// RedisLocks will automatically extend if the process that created the RedisLock is still alive and the RedisLock hasn't been disposed.</param>
		/// <param name="waitTime">How long to block for until a lock can be acquired.</param>
		/// <param name="retryTime">How long to wait between retries when trying to acquire a lock.</param>
		/// <param name="cancellationToken">CancellationToken to abort waiting for blocking lock.</param>
		/// <returns>A RedisLock object.</returns>
		public RedisLock Create(string resource, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime, CancellationToken cancellationToken)
		{
			return RedisLock.Create(redisCaches, resource, expiryTime, waitTime, retryTime, cancellationToken);
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
			return await RedisLock.CreateAsync(redisCaches, resource, expiryTime, waitTime, retryTime).ConfigureAwait(false);
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
		/// <param name="cancellationToken">CancellationToken to abort waiting for blocking lock.</param>
		/// <returns>A RedisLock object.</returns>
		public async Task<RedisLock> CreateAsync(string resource, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime, CancellationToken cancellationToken)
		{
			return await RedisLock.CreateAsync(redisCaches, resource, expiryTime, waitTime, retryTime, cancellationToken).ConfigureAwait(false);
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