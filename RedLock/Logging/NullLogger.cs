using System;

namespace RedLock.Logging
{
	public class NullLogger : IRedLockLogger
	{
		public void DebugWrite(string format, params object[] args)
		{
		}

		public void InfoWrite(string format, params object[] args)
		{
		}

		public void ErrorWrite(string format, params object[] args)
		{
		}

		public void ErrorWrite(Exception exception)
		{
		}
	}
}