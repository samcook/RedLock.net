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
	}
}