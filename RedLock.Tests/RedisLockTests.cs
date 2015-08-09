using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using log4net.Config;
using NUnit.Framework;
using RedLock.Logging;
using RedLock.Logging.Log4Net;
using RedLock.Util;

namespace RedLock.Tests
{
	[TestFixture]
	public class RedisLockTests
	{
		private IRedLockLogger logger;

		[TestFixtureSetUp]
		public void TestFixtureSetUp()
		{
			XmlConfigurator.Configure();

			logger = new Log4NetLogger();
		}

		// make sure redis is running on these
		private static readonly DnsEndPoint ActiveServer1 = new DnsEndPoint("localhost", 6379);
		private static readonly DnsEndPoint ActiveServer2 = new DnsEndPoint("localhost", 6380);
		private static readonly DnsEndPoint ActiveServer3 = new DnsEndPoint("localhost", 6381);

		// make sure redis isn't running on these
		private static readonly DnsEndPoint InactiveServer1 = new DnsEndPoint("localhost", 63790);
		private static readonly DnsEndPoint InactiveServer2 = new DnsEndPoint("localhost", 63791);
		private static readonly DnsEndPoint InactiveServer3 = new DnsEndPoint("localhost", 63791);

		// make sure redis is running here with the specified password
		private static readonly RedisLockEndPoint PasswordedServer = new RedisLockEndPoint
		{
			EndPoint = new DnsEndPoint("localhost", 6382),
			Password = "password"
		};

		private static readonly RedisLockEndPoint NonDefaultDatabaseServer = new RedisLockEndPoint
		{
			EndPoint = ActiveServer1,
			RedisDatabase = 1
		};

		private static readonly RedisLockEndPoint NonDefaultRedisKeyFormatServer = new RedisLockEndPoint
		{
			EndPoint = ActiveServer1,
			RedisKeyFormat = "{0}-redislock"
		};

		private static readonly IEnumerable<EndPoint> AllActiveEndPoints = new[]
		{
			ActiveServer1,
			ActiveServer2,
			ActiveServer3
		};

		private static readonly IEnumerable<EndPoint> AllInactiveEndPoints = new[]
		{
			InactiveServer1,
			InactiveServer2,
			InactiveServer3
		};

		private static readonly IEnumerable<EndPoint> SomeActiveEndPointsWithQuorum = new[]
		{
			ActiveServer1,
			ActiveServer2,
			ActiveServer3,
			InactiveServer1,
			InactiveServer2
		};

		private static readonly IEnumerable<EndPoint> SomeActiveEndPointsWithNoQuorum = new[]
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
			CheckSingleRedisLock(SomeActiveEndPointsWithQuorum, true);
		}

		[Test]
		public void TestOverlappingLocks()
		{
			using (var redisLockFactory = new RedisLockFactory(AllActiveEndPoints, logger))
			{
				var resource = String.Format("testredislock-{0}", Guid.NewGuid());

				using (var firstLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(30)))
				{
					Assert.That(firstLock.IsAcquired, Is.True);

					using (var secondLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(30)))
					{
						Assert.That(secondLock.IsAcquired, Is.False);
					}
				}
			}
		}

		[Test]
		public async Task TestOverlappingLocksAsync()
		{
			var foo = DoOverlappingLocksAsync();

			logger.InfoWrite("======================================================");

			await foo;
		}

		private async Task DoOverlappingLocksAsync()
		{
			using (var redisLockFactory = new RedisLockFactory(AllActiveEndPoints, logger))
			{
				var resource = String.Format("testredislock-{0}", Guid.NewGuid());

				using (var firstLock = await redisLockFactory.CreateAsync(resource, TimeSpan.FromSeconds(30)))
				{
					Assert.That(firstLock.IsAcquired, Is.True);

					using (var secondLock = await redisLockFactory.CreateAsync(resource, TimeSpan.FromSeconds(30)))
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
			
			using (var redisLockFactory = new RedisLockFactory(AllActiveEndPoints, logger))
			{
				var resource = String.Format("testblockingconcurrentlocks-{0}", Guid.NewGuid());

				var threads = new List<Thread>();

				for (var i = 0; i < 2; i++)
				{
					var thread = new Thread(() =>
					{
						// ReSharper disable once AccessToDisposedClosure (we join on threads before disposing)
						using (var redisLock = redisLockFactory.Create(
							resource,
							TimeSpan.FromSeconds(2),
							TimeSpan.FromSeconds(10),
							TimeSpan.FromSeconds(0.5)))
						{
							logger.InfoWrite("Entering lock");
							if (redisLock.IsAcquired)
							{
								Interlocked.Increment(ref locksAcquired);
							}
							Thread.Sleep(4000);
							logger.InfoWrite("Leaving lock");
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
			using (var redisLockFactory = new RedisLockFactory(AllActiveEndPoints, logger))
			{
				var resource = String.Format("testredislock-{0}", Guid.NewGuid());

				using (var firstLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(30)))
				{
					Assert.That(firstLock.IsAcquired, Is.True);
				}

				using (var secondLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(30)))
				{
					Assert.That(secondLock.IsAcquired, Is.True);
				}
			}
		}

		[Test]
		public void TestRenewing()
		{
			using (var redisLockFactory = new RedisLockFactory(AllActiveEndPoints, logger))
			{
				var resource = String.Format("testrenewinglock-{0}", Guid.NewGuid());

				int extendCount;

				using (var redisLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(2)))
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
			using (var lockFactory = new RedisLockFactory(AllActiveEndPoints, logger))
			{
				var resource = String.Format("testrenewinglock-{0}", Guid.NewGuid());

				using (var firstLock = lockFactory.Create(resource, TimeSpan.FromSeconds(1)))
				{
					Assert.That(firstLock.IsAcquired, Is.True);

					Thread.Sleep(550); // should cause keep alive timer to fire once
					firstLock.StopKeepAliveTimer(); // stop the keep alive timer to simulate process crash
					Thread.Sleep(1200); // wait until the key expires from redis

					using (var secondLock = lockFactory.Create(resource, TimeSpan.FromSeconds(1)))
					{
						Assert.That(secondLock.IsAcquired, Is.True); // Eventually the outer lock should timeout
					}
				}
			}
		}

		[Test]
		public void TestQuorum()
		{
			logger.InfoWrite("======== Testing quorum with all active endpoints ========");
			CheckSingleRedisLock(AllActiveEndPoints, true);
			logger.InfoWrite("======== Testing quorum with no active endpoints ========");
			CheckSingleRedisLock(AllInactiveEndPoints, false);
			logger.InfoWrite("======== Testing quorum with enough active endpoints ========");
			CheckSingleRedisLock(SomeActiveEndPointsWithQuorum, true);
			logger.InfoWrite("======== Testing quorum with not enough active endpoints ========");
			CheckSingleRedisLock(SomeActiveEndPointsWithNoQuorum, false);
		}

		[Test]
		public void TestRaceForQuorumMultiple()
		{
			for (var i = 0; i < 2; i++)
			{
				logger.InfoWrite("======== Start test {0} ========", i);

				TestRaceForQuorum();
			}
		}

		[Test]
		public void TestRaceForQuorum()
		{
			var locksAcquired = 0;

			var lockKey = String.Format("testredislock-{0}", ThreadSafeRandom.Next(10000));

			var tasks = new List<Task>();

			for (var i = 0; i < 3; i++)
			{
				var task = new Task(() =>
				{
					logger.DebugWrite("Starting task");

					using (var redisLockFactory = new RedisLockFactory(AllActiveEndPoints, logger))
					{
						var sw = Stopwatch.StartNew();

						using (var redisLock = redisLockFactory.Create(lockKey, TimeSpan.FromSeconds(30)))
						{
							sw.Stop();

							logger.DebugWrite("Lock method took {0}ms to return, IsAcquired = {1}", sw.ElapsedMilliseconds, redisLock.IsAcquired);

							if (redisLock.IsAcquired)
							{
								logger.DebugWrite("Got lock with id {0}, sleeping for a bit", redisLock.LockId);

								Interlocked.Increment(ref locksAcquired);

								// Sleep for long enough for the other threads to give up
								//Thread.Sleep(TimeSpan.FromSeconds(2));
								Task.Delay(TimeSpan.FromSeconds(2)).Wait();

								logger.DebugWrite("Lock with id {0} done sleeping", redisLock.LockId);
							}
							else
							{
								logger.DebugWrite("Couldn't get lock, giving up");
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
			CheckSingleRedisLock(new[] {PasswordedServer}, true);
		}

		[Test]
		[Ignore("Requires a redis server that supports SSL")]
		public void TestSslConnection()
		{
			var endPoint = new RedisLockEndPoint
			{
				EndPoint = new DnsEndPoint("localhost", 6383),
				Ssl = true
			};

			CheckSingleRedisLock(new[] {endPoint}, true);
		}

		[Test]
		public void TestNonDefaultRedisDatabases()
		{
			CheckSingleRedisLock(new[] {NonDefaultDatabaseServer}, true);
		}

		[Test]
		public void TestNonDefaultRedisKeyFormat()
		{
			CheckSingleRedisLock(new[] {NonDefaultRedisKeyFormatServer}, true);
		}

		private void CheckSingleRedisLock(IEnumerable<RedisLockEndPoint> endPoints, bool expectedToAcquire)
		{
			using (var redisLockFactory = new RedisLockFactory(endPoints, logger))
			{
				var resource = String.Format("testredislock-{0}", Guid.NewGuid());

				using (var redisLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(30)))
				{
					Assert.That(redisLock.IsAcquired, Is.EqualTo(expectedToAcquire));
				}
			}
		}
		
		private void CheckSingleRedisLock(IEnumerable<EndPoint> endPoints, bool expectedToAcquire)
		{
			CheckSingleRedisLock(endPoints.Select(x => new RedisLockEndPoint {EndPoint = x}), expectedToAcquire);
		}

		[Test]
		[Ignore]
		public void TimeLock()
		{
			var l = new TraceLogger
			{
				DebugEnabled = false,
				ErrorEnabled = false
			}; 
			//var l = logger;

			using (var redisLockFactory = new RedisLockFactory(AllActiveEndPoints, l))
			{
				var resource = String.Format("testredislock-{0}", Guid.NewGuid());

				for (var i = 0; i < 10; i++)
				{
					var sw = Stopwatch.StartNew();

					using (var redisLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(30)))
					{
						sw.Stop();

						l.InfoWrite("Acquire {0} took {1} ticks, success {2}", i, sw.ElapsedTicks, redisLock.IsAcquired);

						sw.Restart();
					}

					sw.Stop();

					l.InfoWrite("Release {0} took {1} ticks, success", i, sw.ElapsedTicks);
				}
			}
		}
	}
}
