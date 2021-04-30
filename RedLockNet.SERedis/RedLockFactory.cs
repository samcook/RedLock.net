using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RedLockNet.SERedis.Configuration;
using RedLockNet.SERedis.Events;
using RedLockNet.SERedis.Internal;

namespace RedLockNet.SERedis
{
	public class RedLockFactory : IDistributedLockFactory, IDisposable
	{
		private readonly RedLockConfiguration configuration;
		private readonly ILoggerFactory loggerFactory;
		private readonly ICollection<RedisConnection> redisCaches;

		public event EventHandler<RedLockConfigurationChangedEventArgs> ConfigurationChanged;

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

			SubscribeToConnectionEvents();
		}

		private void SubscribeToConnectionEvents()
		{
			foreach (var cache in this.redisCaches)
			{
				cache.ConnectionMultiplexer.ConfigurationChanged += MultiplexerConfigurationChanged;
			}
		}

		private void UnsubscribeFromConnectionEvents()
		{
			foreach (var cache in this.redisCaches)
			{
				cache.ConnectionMultiplexer.ConfigurationChanged -= MultiplexerConfigurationChanged;
			}
		}

		private void MultiplexerConfigurationChanged(object sender, StackExchange.Redis.EndPointEventArgs args)
		{
			RaiseConfigurationChanged();
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
			UnsubscribeFromConnectionEvents();

			this.configuration.ConnectionProvider.DisposeConnections();
		}

		protected virtual void RaiseConfigurationChanged()
		{
			if (ConfigurationChanged == null)
			{
				return;
			}

			var connections = new List<Dictionary<EndPoint, RedLockConfigurationChangedEventArgs.RedLockEndPointStatus>>();

			foreach (var cache in this.redisCaches)
			{
				var endPointStatuses = new Dictionary<EndPoint, RedLockConfigurationChangedEventArgs.RedLockEndPointStatus>();

				foreach (var endPoint in cache.ConnectionMultiplexer.GetEndPoints())
				{
					var server = cache.ConnectionMultiplexer.GetServer(endPoint);

					endPointStatuses.Add(endPoint, new RedLockConfigurationChangedEventArgs.RedLockEndPointStatus(endPoint, server.IsConnected, server.IsReplica));
				}

				connections.Add(endPointStatuses);
			}

			ConfigurationChanged?.Invoke(this, new RedLockConfigurationChangedEventArgs(connections));
		}
	}
}