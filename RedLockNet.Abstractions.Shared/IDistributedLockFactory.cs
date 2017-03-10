using System;
using System.Threading;
using System.Threading.Tasks;

namespace RedLockNet
{
	public interface IDistributedLockFactory
	{
		/// <summary>
		/// Gets a RedLock using the factory's set of redis endpoints. You should check the IsAcquired property before performing actions.
		/// Blocks and retries up to the specified time limits.
		/// </summary>
		/// <param name="resource">The resource string to lock on. Only one RedLock should be acquired for any given resource at once.</param>
		/// <param name="expiryTime">How long the lock should be held for.
		/// RedLocks will automatically extend if the process that created the RedLock is still alive and the RedLock hasn't been disposed.</param>
		/// <returns>A RedLock object.</returns>
		IRedLock CreateLock(string resource, TimeSpan expiryTime);

		/// <summary>
		/// Gets a RedLock using the factory's set of redis endpoints. You should check the IsAcquired property before performing actions.
		/// </summary>
		/// <param name="resource">The resource string to lock on. Only one RedLock should be acquired for any given resource at once.</param>
		/// <param name="expiryTime">How long the lock should be held for.
		/// RedLocks will automatically extend if the process that created the RedLock is still alive and the RedLock hasn't been disposed.</param>
		/// <returns>A RedLock object.</returns>
		Task<IRedLock> CreateLockAsync(string resource, TimeSpan expiryTime);

		/// <summary>
		/// Gets a RedLock using the factory's set of redis endpoints. You should check the IsAcquired property before performing actions.
		/// Blocks and retries up to the specified time limits.
		/// </summary>
		/// <param name="resource">The resource string to lock on. Only one RedLock should be acquired for any given resource at once.</param>
		/// <param name="expiryTime">How long the lock should be held for.
		/// RedLocks will automatically extend if the process that created the RedLock is still alive and the RedLock hasn't been disposed.</param>
		/// <param name="waitTime">How long to block for until a lock can be acquired.</param>
		/// <param name="retryTime">How long to wait between retries when trying to acquire a lock.</param>
		/// <param name="cancellationToken">CancellationToken to abort waiting for blocking lock.</param>
		/// <returns>A RedLock object.</returns>
		IRedLock CreateLock(string resource, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime, CancellationToken? cancellationToken = null);

		/// <summary>
		/// Gets a RedLock using the factory's set of redis endpoints. You should check the IsAcquired property before performing actions.
		/// Blocks and retries up to the specified time limits.
		/// </summary>
		/// <param name="resource">The resource string to lock on. Only one RedLock should be acquired for any given resource at once.</param>
		/// <param name="expiryTime">How long the lock should be held for.
		/// RedLocks will automatically extend if the process that created the RedLock is still alive and the RedLock hasn't been disposed.</param>
		/// <param name="waitTime">How long to block for until a lock can be acquired.</param>
		/// <param name="retryTime">How long to wait between retries when trying to acquire a lock.</param>
		/// <param name="cancellationToken">CancellationToken to abort waiting for blocking lock.</param>
		/// <returns>A RedLock object.</returns>
		Task<IRedLock> CreateLockAsync(string resource, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime, CancellationToken? cancellationToken = null);
	}
}