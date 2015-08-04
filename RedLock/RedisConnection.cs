using StackExchange.Redis;

namespace RedLock
{
	internal class RedisConnection
	{
		public ConnectionMultiplexer ConnectionMultiplexer { get; set; }
		public int RedisDatabase { get; set; }
		public string RedisKeyFormat { get; set; }
	}
}