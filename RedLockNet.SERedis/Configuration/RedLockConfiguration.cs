using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace RedLockNet.SERedis.Configuration
{
	public class RedLockConfiguration
	{
		public RedLockConfiguration(IList<RedLockEndPoint> endPoints, ILoggerFactory loggerFactory = null)
		{
			this.ConnectionProvider = new InternallyManagedRedLockConnectionProvider(loggerFactory)
			{
				EndPoints = endPoints
			};
			this.LoggerFactory = loggerFactory;
		}

		public RedLockConfiguration(RedLockConnectionProvider connectionProvider, ILoggerFactory loggerFactory = null)
		{
			this.ConnectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider), "Connection provider must not be null");
			this.LoggerFactory = loggerFactory;
		}

		public RedLockConnectionProvider ConnectionProvider { get; }
		public ILoggerFactory LoggerFactory { get; }
		public RedLockRetryConfiguration RetryConfiguration { get; set; }
	}

	public class RedLockRetryConfiguration
	{
		public RedLockRetryConfiguration(int retryCount = 3, int retryDelayMs = 400)
		{
			if(retryCount < 1)
				throw new ArgumentException("Retry count must be at least 1");

			if(retryDelayMs < 10)
				throw new ArgumentException("Retry delay must be at least 10 ms");

			RetryCount = retryCount;
			RetryDelayMs = retryDelayMs;
		}

		public int RetryCount { get; }

		public int RetryDelayMs { get; }
	}
}