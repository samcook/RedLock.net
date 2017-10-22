using System.Collections.Generic;
using RedLockNet.SERedis.Internal;

namespace RedLockNet.SERedis.Configuration
{
	public abstract class RedLockConnectionProvider
	{
		internal abstract ICollection<RedisConnection> CreateRedisConnections();
		internal abstract void DisposeConnections();

		protected const int DefaultRedisDatabase = -1;
		protected const string DefaultRedisKeyFormat = "redlock:{0}";
	}
}