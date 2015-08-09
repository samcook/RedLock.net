using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RedLock.Util
{
	internal static class TaskUtils
	{
		public static Task Delay(TimeSpan timeSpan)
		{
			return Delay((int) timeSpan.TotalMilliseconds);
		}

		public static Task Delay(int delayMs)
		{
#if NET40
			return TaskEx.Delay(delayMs);
#else
			return Task.Delay(delayMs);
#endif
		}

		public static Task<T[]> WhenAll<T>(IEnumerable<Task<T>> tasks)
		{
#if NET40
			return TaskEx.WhenAll(tasks);
#else
			return Task.WhenAll(tasks);
#endif
		}
	}
}