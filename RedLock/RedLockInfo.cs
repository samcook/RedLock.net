using System;

namespace RedLock
{
	public class RedLockInfo
	{
		public TimeSpan Validity { get; set; }
		public string Resource { get; set; }
		public string LockId { get; set; }
	}
}