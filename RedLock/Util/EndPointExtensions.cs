using System.Net;

namespace RedLock.Util
{
	internal static class EndPointExtensions
	{
		internal static string GetFriendlyName(this EndPoint endPoint)
		{
			var dnsEndPoint = endPoint as DnsEndPoint;

			if (dnsEndPoint != null)
			{
				return $"{dnsEndPoint.Host}:{dnsEndPoint.Port}";
			}

			var ipEndPoint = endPoint as IPEndPoint;

			if (ipEndPoint != null)
			{
				return $"{ipEndPoint.Address}:{ipEndPoint.Port}";
			}

			return endPoint.ToString();
		}
	}
}