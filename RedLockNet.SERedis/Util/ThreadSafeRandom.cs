using System;

namespace RedLockNet.SERedis.Util
{
	internal static class ThreadSafeRandom
	{
		private static readonly Random GlobalRandom = new Random();
		
		[ThreadStatic]
		private static Random localRandom;

		public static int Next()
		{
			return GetLocalRandom().Next();
		}

		public static int Next(int maxValue)
		{
			return GetLocalRandom().Next(maxValue);
		}

		public static int Next(int minValue, int maxValue)
		{
			return GetLocalRandom().Next(minValue, maxValue);
		}

		public static double NextDouble()
		{
			return GetLocalRandom().NextDouble();
		}

		public static void NextBytes(byte[] buffer)
		{
			GetLocalRandom().NextBytes(buffer);
		}

		private static Random GetLocalRandom()
		{
			if (localRandom == null)
			{
				lock (GlobalRandom)
				{
					if (localRandom == null)
					{
						var seed = GlobalRandom.Next();
						localRandom = new Random(seed);
					}
				}
			}

			return localRandom;
		}
	}
}
