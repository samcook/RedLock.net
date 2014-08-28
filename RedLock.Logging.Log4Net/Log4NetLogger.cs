using System;
using log4net;

namespace RedLock.Logging.Log4Net
{
    public class Log4NetLogger : IRedLockLogger
    {
	    private static readonly ILog Log = LogManager.GetLogger("RedLock");

	    public void DebugWrite(string format, params object[] args)
	    {
		    Log.DebugFormat(format, args);
	    }

	    public void InfoWrite(string format, params object[] args)
	    {
			Log.InfoFormat(format, args);
	    }

	    public void ErrorWrite(string format, params object[] args)
	    {
			Log.ErrorFormat(format, args);
	    }

	    public void ErrorWrite(Exception exception)
	    {
			Log.Error("Exception", exception);
	    }
    }
}
