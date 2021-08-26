using System;

namespace RedLockNet.SERedis.Configuration
{
	public class RedLockRetryConfiguration
	{
		public RedLockRetryConfiguration(int? retryCount = null, int? retryDelayMs = null)
		{
			if (retryCount.HasValue && retryCount < 1)
			{
				throw new ArgumentException("Retry count must be at least 1", nameof(retryCount));
			}

			if (retryDelayMs.HasValue && retryDelayMs < 10)
			{
				throw new ArgumentException("Retry delay must be at least 10 ms", nameof(retryDelayMs));
			}

			RetryCount = retryCount;
			RetryDelayMs = retryDelayMs;
		}

		public int? RetryCount { get; }

		public int? RetryDelayMs { get; }
	}
}