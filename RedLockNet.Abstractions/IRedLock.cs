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
		/// The status of the lock.
		/// </summary>
		RedLockStatus Status { get; }

		/// <summary>
		/// Details of the number of instances the lock was able to be acquired in.
		/// </summary>
		RedLockInstanceSummary InstanceSummary { get; }

		/// <summary>
		/// The number of times the lock has been extended.
		/// </summary>
		int ExtendCount { get; }

        /// <summary>
        /// Triggered whenever status property changes.
        /// </summary>
        event EventHandler OnStatusChanged;
	}
}