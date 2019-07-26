using System;

namespace RedLockNet.SERedis.Events
{
	public class RedLockStatusChangedEventArgs<T> : EventArgs
	{
		public T OldValue { get; }
		public T NewValue { get; }

		public RedLockStatusChangedEventArgs(T oldValue, T newValue)
		{
			OldValue = oldValue;
			NewValue = newValue;
		}
	}
}
