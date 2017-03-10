using StackExchange.Redis;

namespace RedLockNet.SERedis.Internal
{
	internal class RedisConnection
	{
		public IConnectionMultiplexer ConnectionMultiplexer { get; set; }
		public int RedisDatabase { get; set; }
		public string RedisKeyFormat { get; set; }
	}
}