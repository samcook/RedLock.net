﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using RedLockNet.SERedis.Util;
using StackExchange.Redis;

namespace RedLockNet.Tests
{
	[TestFixture]
	[Category("RedLockTests")]
	public class RedLockTests
	{
		private ILoggerFactory loggerFactory;
		private ILogger logger;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			loggerFactory = new LoggerFactory().AddConsole(LogLevel.Debug);
			logger = loggerFactory.CreateLogger<RedLockTests>();
		}

		// make sure redis is running on these
		private static readonly EndPoint ActiveServer1 = new DnsEndPoint("localhost", 6379);
		private static readonly EndPoint ActiveServer2 = new DnsEndPoint("localhost", 6380);
		private static readonly EndPoint ActiveServer3 = new DnsEndPoint("localhost", 6381);

		// make sure redis isn't running on these
		private static readonly EndPoint InactiveServer1 = new DnsEndPoint("localhost", 63790);
		private static readonly EndPoint InactiveServer2 = new DnsEndPoint("localhost", 63791);
		private static readonly EndPoint InactiveServer3 = new DnsEndPoint("localhost", 63791);

		// make sure redis is running here with the specified password
		private static readonly RedLockEndPoint PasswordedServer = new RedLockEndPoint
		{
			EndPoint = new DnsEndPoint("localhost", 6382),
			Password = "password"
		};

		private static readonly RedLockEndPoint NonDefaultDatabaseServer = new RedLockEndPoint
		{
			EndPoint = ActiveServer1,
			RedisDatabase = 1
		};

		private static readonly RedLockEndPoint NonDefaultRedisKeyFormatServer = new RedLockEndPoint
		{
			EndPoint = ActiveServer1,
			RedisKeyFormat = "{0}-redislock"
		};

		private static readonly IList<RedLockEndPoint> AllActiveEndPoints = new List<RedLockEndPoint>
		{
			ActiveServer1,
			ActiveServer2,
			ActiveServer3
		};

		private static readonly IList<RedLockEndPoint> AllInactiveEndPoints = new List<RedLockEndPoint>
		{
			InactiveServer1,
			InactiveServer2,
			InactiveServer3
		};

		private static readonly IList<RedLockEndPoint> SomeActiveEndPointsWithQuorum = new List<RedLockEndPoint>
		{
			ActiveServer1,
			ActiveServer2,
			ActiveServer3,
			InactiveServer1,
			InactiveServer2
		};

		private static readonly IList<RedLockEndPoint> SomeActiveEndPointsWithNoQuorum = new List<RedLockEndPoint>
		{
			ActiveServer1,
			ActiveServer2,
			ActiveServer3,
			InactiveServer1,
			InactiveServer2,
			InactiveServer3
		};


		[Test]
		public void TestSingleLock()
		{
			CheckSingleRedisLock(
				() => RedLockFactory.Create(SomeActiveEndPointsWithQuorum, loggerFactory),
				true);
		}

		[Test]
		public void TestOverlappingLocks()
		{
			using (var redisLockFactory = RedLockFactory.Create(AllActiveEndPoints, loggerFactory))
			{
				var resource = $"testredislock:{Guid.NewGuid()}";

				using (var firstLock = redisLockFactory.CreateLock(resource, TimeSpan.FromSeconds(30)))
				{
					Assert.That(firstLock.IsAcquired, Is.True);

					using (var secondLock = redisLockFactory.CreateLock(resource, TimeSpan.FromSeconds(30)))
					{
						Assert.That(secondLock.IsAcquired, Is.False);
					}
				}
			}
		}

		[Test]
		public async Task TestOverlappingLocksAsync()
		{
			var task = DoOverlappingLocksAsync();

			logger.LogInformation("======================================================");

			await task;
		}

		private async Task DoOverlappingLocksAsync()
		{
			using (var redisLockFactory = RedLockFactory.Create(AllActiveEndPoints, loggerFactory))
			{
				var resource = $"testredislock:{Guid.NewGuid()}";

				using (var firstLock = await redisLockFactory.CreateLockAsync(resource, TimeSpan.FromSeconds(30)))
				{
					Assert.That(firstLock.IsAcquired, Is.True);

					using (var secondLock = await redisLockFactory.CreateLockAsync(resource, TimeSpan.FromSeconds(30)))
					{
						Assert.That(secondLock.IsAcquired, Is.False);
					}
				}
			}
		}

		[Test]
		public void TestBlockingConcurrentLocks()
		{
			var locksAcquired = 0;
			
			using (var redisLockFactory = RedLockFactory.Create(AllActiveEndPoints, loggerFactory))
			{
				var resource = $"testblockingconcurrentlocks:{Guid.NewGuid()}";

				var threads = new List<Thread>();

				for (var i = 0; i < 2; i++)
				{
					var thread = new Thread(() =>
					{
						// ReSharper disable once AccessToDisposedClosure (we join on threads before disposing)
						using (var redisLock = redisLockFactory.CreateLock(
							resource,
							TimeSpan.FromSeconds(2),
							TimeSpan.FromSeconds(10),
							TimeSpan.FromSeconds(0.5)))
						{
							logger.LogInformation("Entering lock");
							if (redisLock.IsAcquired)
							{
								Interlocked.Increment(ref locksAcquired);
							}
							Thread.Sleep(4000);
							logger.LogInformation("Leaving lock");
						}
					});

					thread.Start();

					threads.Add(thread);
				}

				foreach (var thread in threads)
				{
					thread.Join();
				}
			}

			Assert.That(locksAcquired, Is.EqualTo(2));
		}

		[Test]
		public void TestSequentialLocks()
		{
			using (var redisLockFactory = RedLockFactory.Create(AllActiveEndPoints, loggerFactory))
			{
				var resource = $"testredislock:{Guid.NewGuid()}";

				using (var firstLock = redisLockFactory.CreateLock(resource, TimeSpan.FromSeconds(30)))
				{
					Assert.That(firstLock.IsAcquired, Is.True);
				}

				using (var secondLock = redisLockFactory.CreateLock(resource, TimeSpan.FromSeconds(30)))
				{
					Assert.That(secondLock.IsAcquired, Is.True);
				}
			}
		}

		[Test]
		public void TestRenewing()
		{
			using (var redisLockFactory = RedLockFactory.Create(AllActiveEndPoints, loggerFactory))
			{
				var resource = $"testrenewinglock:{Guid.NewGuid()}";

				int extendCount;

				using (var redisLock = redisLockFactory.CreateLock(resource, TimeSpan.FromSeconds(2)))
				{
					Assert.That(redisLock.IsAcquired, Is.True);

					Thread.Sleep(4000);

					extendCount = redisLock.ExtendCount;
				}

				Assert.That(extendCount, Is.GreaterThan(2));
			}
		}

		[Test]
		public void TestLockReleasedAfterTimeout()
		{
			using (var lockFactory = RedLockFactory.Create(AllActiveEndPoints, loggerFactory))
			{
				var resource = $"testrenewinglock:{Guid.NewGuid()}";

				using (var firstLock = lockFactory.CreateLock(resource, TimeSpan.FromSeconds(1)))
				{
					Assert.That(firstLock.IsAcquired, Is.True);

					Thread.Sleep(550); // should cause keep alive timer to fire once
					((RedLock)firstLock).StopKeepAliveTimer(); // stop the keep alive timer to simulate process crash
					Thread.Sleep(1200); // wait until the key expires from redis

					using (var secondLock = lockFactory.CreateLock(resource, TimeSpan.FromSeconds(1)))
					{
						Assert.That(secondLock.IsAcquired, Is.True); // Eventually the outer lock should timeout
					}
				}
			}
		}

		[Test]
		public void TestQuorum()
		{
			logger.LogInformation("======== Testing quorum with all active endpoints ========");
			CheckSingleRedisLock(
				() => RedLockFactory.Create(AllActiveEndPoints, loggerFactory),
				true);
			logger.LogInformation("======== Testing quorum with no active endpoints ========");
			CheckSingleRedisLock(
				() => RedLockFactory.Create(AllInactiveEndPoints, loggerFactory),
				false);
			logger.LogInformation("======== Testing quorum with enough active endpoints ========");
			CheckSingleRedisLock(
				() => RedLockFactory.Create(SomeActiveEndPointsWithQuorum, loggerFactory),
				true);
			logger.LogInformation("======== Testing quorum with not enough active endpoints ========");
			CheckSingleRedisLock(
				() => RedLockFactory.Create(SomeActiveEndPointsWithNoQuorum, loggerFactory),
				false);
		}

		[Test]
		public void TestRaceForQuorumMultiple()
		{
			for (var i = 0; i < 2; i++)
			{
				logger.LogInformation($"======== Start test {i} ========");

				TestRaceForQuorum();
			}
		}

		[Test]
		public void TestRaceForQuorum()
		{
			var locksAcquired = 0;

			var lockKey = $"testredislock:{ThreadSafeRandom.Next(10000)}";

			var tasks = new List<Task>();

			for (var i = 0; i < 3; i++)
			{
				var task = new Task(() =>
				{
					logger.LogDebug("Starting task");

					using (var redisLockFactory = RedLockFactory.Create(AllActiveEndPoints, loggerFactory))
					{
						var sw = Stopwatch.StartNew();

						using (var redisLock = redisLockFactory.CreateLock(lockKey, TimeSpan.FromSeconds(30)))
						{
							sw.Stop();

							logger.LogDebug($"Lock method took {sw.ElapsedMilliseconds}ms to return, IsAcquired = {redisLock.IsAcquired}");

							if (redisLock.IsAcquired)
							{
								logger.LogDebug($"Got lock with id {redisLock.LockId}, sleeping for a bit");

								Interlocked.Increment(ref locksAcquired);

								// Sleep for long enough for the other threads to give up
								//Thread.Sleep(TimeSpan.FromSeconds(2));
								Task.Delay(TimeSpan.FromSeconds(2)).Wait();

								logger.LogDebug($"Lock with id {redisLock.LockId} done sleeping");
							}
							else
							{
								logger.LogDebug("Couldn't get lock, giving up");
							}
						}
					}
				}, TaskCreationOptions.LongRunning);

				tasks.Add(task);
			}

			foreach (var task in tasks)
			{
				task.Start();
			}

			Task.WaitAll(tasks.ToArray());

			Assert.That(locksAcquired, Is.EqualTo(1));
		}

		[Test]
		public void TestPasswordConnection()
		{
			CheckSingleRedisLock(
				() => RedLockFactory.Create(new List<RedLockEndPoint> { PasswordedServer }, loggerFactory),
				true);
		}

		[Test]
		[Ignore("Requires a redis server that supports SSL")]
		public void TestSslConnection()
		{
			var endPoint = new RedLockEndPoint
			{
				EndPoint = new DnsEndPoint("localhost", 6383),
				Ssl = true
			};

			CheckSingleRedisLock(
				() => RedLockFactory.Create(new List<RedLockEndPoint> { endPoint }, loggerFactory),
				true);
		}

		[Test]
		public void TestNonDefaultRedisDatabases()
		{
			CheckSingleRedisLock(
				() => RedLockFactory.Create(new List<RedLockEndPoint> { NonDefaultDatabaseServer }, loggerFactory),
				true);
		}

		[Test]
		public void TestNonDefaultRedisKeyFormat()
		{
			CheckSingleRedisLock(
				() => RedLockFactory.Create(new List<RedLockEndPoint> {NonDefaultRedisKeyFormatServer}, loggerFactory),
				true);
		}

		private static void CheckSingleRedisLock([InstantHandle]Func<RedLockFactory> factoryBuilder, bool expectedToAcquire)
		{
			using (var redisLockFactory = factoryBuilder())
			{
				var resource = $"testredislock:{Guid.NewGuid()}";

				using (var redisLock = redisLockFactory.CreateLock(resource, TimeSpan.FromSeconds(30)))
				{
					Assert.That(redisLock.IsAcquired, Is.EqualTo(expectedToAcquire));
				}
			}
		}
		
		[Test]
		public void TestCancelBlockingLock()
		{
			var cts = new CancellationTokenSource();

			var resource = $"testredislock:{Guid.NewGuid()}";

			using (var redisLockFactory = RedLockFactory.Create(AllActiveEndPoints, loggerFactory))
			{
				using (var firstLock = redisLockFactory.CreateLock(
					resource,
					TimeSpan.FromSeconds(30),
					TimeSpan.FromSeconds(10),
					TimeSpan.FromSeconds(1)))
				{
					Assert.That(firstLock.IsAcquired);

					cts.CancelAfter(TimeSpan.FromSeconds(2));

					Assert.Throws<OperationCanceledException>(() =>
					{
						using (var secondLock = redisLockFactory.CreateLock(
							resource,
							TimeSpan.FromSeconds(30),
							TimeSpan.FromSeconds(10),
							TimeSpan.FromSeconds(1),
							cts.Token))
						{
							// should never get here
							Assert.Fail();
						}
					});
				}
			}
		}

		[Test]
		public void TestFactoryHasAtLeastOneEndPoint()
		{
			Assert.Throws<ArgumentException>(() =>
			{
				using (var redisLockFactory = RedLockFactory.Create(new List<RedLockEndPoint>(), loggerFactory))
				{
				}
			});

			Assert.Throws<ArgumentException>(() =>
			{
				using (var redisLockFactory = RedLockFactory.Create((IList<RedLockEndPoint>) null, loggerFactory))
				{
				}
			});
		}

		[Test]
		public void TestExistingMultiplexers()
		{
			using (var connectionMultiplexer = ConnectionMultiplexer.Connect(new ConfigurationOptions
			{
				AbortOnConnectFail = false,
				EndPoints = {ActiveServer1}
			}))
			{
				CheckSingleRedisLock(
					() => RedLockFactory.Create(new List<RedLockMultiplexer> {connectionMultiplexer}, loggerFactory),
					true);
			}
		}

		[Test]
		[Ignore("Timing test")]
		public void TimeLock()
		{
			using (var redisLockFactory = RedLockFactory.Create(AllActiveEndPoints, loggerFactory))
			{
				var resource = $"testredislock:{Guid.NewGuid()}";

				for (var i = 0; i < 10; i++)
				{
					var sw = Stopwatch.StartNew();

					using (var redisLock = redisLockFactory.CreateLock(resource, TimeSpan.FromSeconds(30)))
					{
						sw.Stop();

						logger.LogInformation($"Acquire {i} took {sw.ElapsedTicks} ticks, success {redisLock.IsAcquired}");

						sw.Restart();
					}

					sw.Stop();

					logger.LogInformation($"Release {i} took {sw.ElapsedTicks} ticks, success");
				}
			}
		}
	}
}
