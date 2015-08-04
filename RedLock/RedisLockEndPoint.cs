using System.Net;

namespace RedLock
{
	public class RedisLockEndPoint
	{
		public EndPoint EndPoint { get; set; }
		public bool Ssl { get; set; }
		public string Password { get; set; }
		public int? ConnectionTimeout { get; set; }
		public int? RedisDatabase { get; set; }
		public string RedisKeyFormat { get; set; }
	}
}
