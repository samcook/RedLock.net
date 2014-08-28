using System;
using System.Diagnostics;
using System.Text;

namespace RedLock.Logging
{
	public class TraceLogger : IRedLockLogger
	{
		public bool DebugEnabled { get; set; }
		public bool InfoEnabled { get; set; }
		public bool ErrorEnabled { get; set; }
		public bool IncludeStackTraces { get; set; }

		public TraceLogger()
		{
			DebugEnabled = true;
			InfoEnabled = true;
			ErrorEnabled = true;
			IncludeStackTraces = true;
		}

		public void DebugWrite(string format, params object[] args)
		{
			if (DebugEnabled)
			{
				SafeTrace(string.Format("DEBUG: " + format, args));
			}
		}

		public void InfoWrite(string format, params object[] args)
		{
			if (InfoEnabled)
			{
				SafeTrace(string.Format("INFO: " + format, args));
			}
		}

		public void ErrorWrite(string format, params object[] args)
		{
			if (ErrorEnabled)
			{
				SafeTrace(string.Format("ERROR: " + format, args));
			}
		}

		public void ErrorWrite(Exception exception)
		{
			if (ErrorEnabled)
			{
				SafeTrace("ERROR: " + GetExceptionString(exception));
			}
		}

		public void ErrorWrite(string message, Exception exception)
		{
			if (ErrorEnabled)
			{
				SafeTrace(string.Format("ERROR: {0}{1}{2}", message, Environment.NewLine, GetExceptionString(exception)));
			}
		}

		private string GetExceptionString(Exception exception)
		{
			var result = new StringBuilder();

			result.Append(IncludeStackTraces
				? exception.ToString()
				: String.Format("{0}: {1}", exception.GetType(), exception.Message));

			if (!IncludeStackTraces && exception.InnerException != null)
			{
				result.Append(" ---> ");
				result.Append(GetExceptionString(exception.InnerException));
			}

			return result.ToString();
		}

		private static void SafeTrace(string message)
		{
			// ReSharper disable once EmptyGeneralCatchClause
			try
			{
				Trace.WriteLine(message);
			}
			catch
			{
			}
		}
	}
}