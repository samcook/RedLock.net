using StackExchange.Redis;

namespace RedLockNet.SERedis.Configuration
{
	public class RedLockMultiplexer
	{
		public IConnectionMultiplexer ConnectionMultiplexer { get; }

		public RedLockMultiplexer(IConnectionMultiplexer connectionMultiplexer)
		{
			this.ConnectionMultiplexer = connectionMultiplexer;
		}

		public static implicit operator RedLockMultiplexer(ConnectionMultiplexer connectionMultiplexer)
		{
			return new RedLockMultiplexer(connectionMultiplexer);
		}

		/// <summary>
		/// The database to use with this redis connection.
		/// Defaults to 0 if not specified.
		/// </summary>
		public int? RedisDatabase { get; set; }

		/// <summary>
		/// The string format for keys created in redis, must include {0}.
		/// Defaults to "redlock-{0}" if not specified.
		/// </summary>
		public string RedisKeyFormat { get; set; }
	}
}