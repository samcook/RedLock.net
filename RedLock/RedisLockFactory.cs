using System;
using System.Collections.Generic;
using StackExchange.Redis;

namespace RedLock
{
	public class RedisLockFactory : IDisposable
	{
		private readonly IList<ConnectionMultiplexer> redisCaches;

		public RedisLockFactory(params Tuple<string, int>[] redisHostPorts)
		{
			redisCaches = CreateRedisCaches(redisHostPorts);
		}

		private static IList<ConnectionMultiplexer> CreateRedisCaches(ICollection<Tuple<string, int>> redisHostPorts)
		{
			var caches = new List<ConnectionMultiplexer>(redisHostPorts.Count);

			foreach (var hostPort in redisHostPorts)
			{
				var configuration = new ConfigurationOptions
				{
					AbortOnConnectFail = false,
					ConnectTimeout = 100
				};

				configuration.EndPoints.Add(hostPort.Item1, hostPort.Item2);

				caches.Add(ConnectionMultiplexer.Connect(configuration));
			}

			return caches;
		}

		public RedisLock Create(string resource, TimeSpan ttl)
		{
			return new RedisLock(redisCaches, resource, ttl);
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