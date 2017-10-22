using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace RedLockNet.SERedis.Events
{
	public class RedLockConfigurationChangedEventArgs : EventArgs
	{
		public class RedLockEndPointStatus
		{
			public EndPoint EndPoint { get; }
			public bool IsConnected { get; }
			public bool IsSlave { get; }

			public RedLockEndPointStatus(EndPoint endPoint, bool isConnected, bool isSlave)
			{
				this.EndPoint = endPoint;
				this.IsConnected = isConnected;
				this.IsSlave = isSlave;
			}
		}

		public ICollection<Dictionary<EndPoint, RedLockEndPointStatus>> EndPointConnections { get; }

		public RedLockConfigurationChangedEventArgs(ICollection<Dictionary<EndPoint, RedLockEndPointStatus>> connections)
		{
			this.EndPointConnections = connections;
		}

		public int Quorum => this.InstancesCount / 2 + 1;
		public bool HasQuorum => this.InstancesWithConnectedMastersCount >= Quorum;

		public int InstancesCount => this.EndPointConnections.Count;
		public int InstancesWithConnectedMastersCount
		{
			get
			{
				var result = 0;

				foreach (var instance in this.EndPointConnections)
				{
					if (instance.Any(x => x.Value.IsConnected && !x.Value.IsSlave))
					{
						result++;
					}
				}

				return result;
			}
		}
	}
}