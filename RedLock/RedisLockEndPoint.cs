using System.Net;

namespace RedLock
{
	public class RedisLockEndPoint
	{
		/// <summary>
		/// The endpoint for the redis connection.
		/// </summary>
		public EndPoint EndPoint { get; set; }
		
		/// <summary>
		/// Whether to use SSL for the redis connection.
		/// </summary>
		public bool Ssl { get; set; }
		
		/// <summary>
		/// The password for the redis connection.
		/// </summary>
		public string Password { get; set; }
		
		/// <summary>
		/// The connection timeout for the redis connection.
		/// Defaults to 100ms if not specified.
		/// </summary>
		public int? ConnectionTimeout { get; set; }
		
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
