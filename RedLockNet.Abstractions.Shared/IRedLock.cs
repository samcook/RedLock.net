using System;

namespace RedLockNet
{
	public interface IRedLock : IDisposable
	{
		string LockId { get; }
		bool IsAcquired { get; }
		int ExtendCount { get; }
	}
}