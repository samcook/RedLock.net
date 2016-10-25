using System;

namespace RedLock
{
	public interface IRedisLock : IDisposable
	{
		string LockId { get; }
		bool IsAcquired { get; }
		int ExtendCount { get; }
	}
}