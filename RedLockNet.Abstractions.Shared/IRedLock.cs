using System;

namespace RedLockNet
{
	public interface IRedLock : IDisposable
	{
		/// <summary>
		/// The name of the resource the lock is for.
		/// </summary>
		string Resource { get; }

		/// <summary>
		/// The unique identifier assigned to this lock.
		/// </summary>
		string LockId { get; }

		/// <summary>
		/// Whether the lock has been acquired.
		/// </summary>
		bool IsAcquired { get; }

		/// <summary>
		/// The number of times the lock has been extended.
		/// </summary>
		int ExtendCount { get; }
	}
}