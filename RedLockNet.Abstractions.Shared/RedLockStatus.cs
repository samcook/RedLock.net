namespace RedLockNet
{
	public enum RedLockStatus
	{
		/// <summary>
		/// The lock has not yet been successfully acquired, or has been released.
		/// </summary>
		Unlocked,

		/// <summary>
		/// The lock was acquired successfully.
		/// </summary>
		Acquired,

		/// <summary>
		/// The lock was not acquired because there was no quorum available.
		/// </summary>
		NoQuorum,

		/// <summary>
		/// The lock was not acquired because it is currently locked with a different LockId.
		/// </summary>
		Conflicted,

		/// <summary>
		/// The lock expiry time passed before lock acquisition could be completed.
		/// </summary>
		Expired
	}
}