using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RedLockNet.SERedis.Configuration;
using RedLockNet.SERedis.Internal;

namespace RedLockNet.SERedis
{
	public class RedLockFactory : IDistributedLockFactory, IDisposable
	{
		private readonly RedLockConfiguration configuration;
		private readonly ILoggerFactory loggerFactory;
		private readonly ICollection<RedisConnection> redisCaches;

		/// <summary>
		/// Create a RedLockFactory using a list of RedLockEndPoints (ConnectionMultiplexers will be internally managed by RedLock.net)
		/// </summary>
		public static RedLockFactory Create(IList<RedLockEndPoint> endPoints, ILoggerFactory loggerFactory = null)
		{
			var configuration = new RedLockConfiguration(endPoints, loggerFactory);
			return new RedLockFactory(configuration);
		}

		/// <summary>
		/// Create a RedLockFactory using existing StackExchange.Redis ConnectionMultiplexers
		/// </summary>
		public static RedLockFactory Create(IList<RedLockMultiplexer> existingMultiplexers, ILoggerFactory loggerFactory = null)
		{
			var configuration = new RedLockConfiguration(
				new ExistingMultiplexersRedLockConnectionProvider
				{
					Multiplexers = existingMultiplexers
				},
				loggerFactory);

			return new RedLockFactory(configuration);
		}

		/// <summary>
		/// Create a RedLockFactory using the specified configuration
		/// </summary>
		public RedLockFactory(RedLockConfiguration configuration)
		{
			this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration), "Configuration must not be null");
			this.loggerFactory = configuration.LoggerFactory ?? new LoggerFactory();
			this.redisCaches = configuration.ConnectionProvider.CreateRedisConnections();
		}

		public IRedLock CreateLock(string resource, TimeSpan expiryTime)
		{
			return RedLock.Create(
				this.loggerFactory.CreateLogger<RedLock>(),
				redisCaches,
				resource,
				expiryTime);
		}

		public async Task<IRedLock> CreateLockAsync(string resource, TimeSpan expiryTime)
		{
			return await RedLock.CreateAsync(
				this.loggerFactory.CreateLogger<RedLock>(),
				redisCaches,
				resource,
				expiryTime).ConfigureAwait(false);
		}

		public IRedLock CreateLock(string resource, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime, CancellationToken? cancellationToken = null)
		{
			return RedLock.Create(
				this.loggerFactory.CreateLogger<RedLock>(),
				redisCaches,
				resource,
				expiryTime,
				waitTime,
				retryTime,
				cancellationToken ?? CancellationToken.None);
		}

		public async Task<IRedLock> CreateLockAsync(string resource, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime, CancellationToken? cancellationToken = null)
		{
			return await RedLock.CreateAsync(
				this.loggerFactory.CreateLogger<RedLock>(),
				redisCaches,
				resource,
				expiryTime,
				waitTime,
				retryTime,
				cancellationToken ?? CancellationToken.None).ConfigureAwait(false);
		}

		public void Dispose()
		{
			this.configuration.ConnectionProvider.DisposeConnections();
		}
	}
}