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

		private IList<ConnectionMultiplexer> CreateRedisCaches(ICollection<EndPoint> redisEndPoints, bool tryToWarmUpConnections = true)
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

				var cache = ConnectionMultiplexer.Connect(configuration);

				if (tryToWarmUpConnections)
				{
					try
					{
						var ping = cache.GetDatabase().Ping();

						logger.DebugWrite("{0} Ping 1 took {1}ms", RedisLock.GetHost(cache), ping.TotalMilliseconds);

						ping = cache.GetDatabase().Ping();

						logger.DebugWrite("{0} Ping 2 took {1}ms", RedisLock.GetHost(cache), ping.TotalMilliseconds);
					}
					// ReSharper disable once EmptyGeneralCatchClause
					catch (Exception)
					{
						// ignore this
					}
				}

				caches.Add(cache);
			}

			return caches;
		}

		public RedisLock Create(string resource, TimeSpan expiryTime)
		{
			return new RedisLock(redisCaches, resource, expiryTime, null, logger);
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