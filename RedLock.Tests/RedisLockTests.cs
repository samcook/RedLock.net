using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using log4net.Config;
using NUnit.Framework;

namespace RedLock.Tests
{
	[TestFixture]
	public class RedisLockTests
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (RedisLockTests));

		[TestFixtureSetUp]
		public void TestFixtureSetUp()
		{
			XmlConfigurator.Configure();
		}

		[Test]
		public void TestOverlappingLocks()
		{
			using (var redisLockFactory = new RedisLockFactory(new[]
			{
				new Tuple<string, int>("localhost", 6379),
				new Tuple<string, int>("localhost", 6380)
			}))
			{
				var resource = String.Format("testredislock-{0}", Guid.NewGuid());

				using (var firstLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(30)))
				{
					Assert.That(firstLock.IsAcquired, Is.True);
					Assert.That(firstLock.LockInfo.Resource, Is.EqualTo(resource));

					using (var secondLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(30)))
					{
						Assert.That(secondLock.IsAcquired, Is.False);
					}
				}
			}
		}

		[Test]
		public void TestSequentialLocks()
		{
			using (var redisLockFactory = new RedisLockFactory(new[]
			{
				new Tuple<string, int>("localhost", 6379),
				new Tuple<string, int>("localhost", 6380)
			}))
			{
				var resource = String.Format("testredislock-{0}", Guid.NewGuid());

				using (var firstLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(30)))
				{
					Assert.That(firstLock.IsAcquired, Is.True);
					Assert.That(firstLock.LockInfo.Resource, Is.EqualTo(resource));
				}

				using (var secondLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(30)))
				{
					Assert.That(secondLock.IsAcquired, Is.True);
					Assert.That(secondLock.LockInfo.Resource, Is.EqualTo(resource));
				}
			}
		}

		[Test]
		public void TestRaceForQuorum()
		{
			var locksAcquired = 0;

			var lockKey = String.Format("testredislock-{0}", Guid.NewGuid());

			var tasks = new List<Task>();

			for (var i = 0; i < 3; i++)
			{
				var task = new Task(() =>
				{
					Log.Debug("Starting task");

					using (var redisLockFactory = new RedisLockFactory(new[]
					{
						new Tuple<string, int>("localhost", 6379),
						new Tuple<string, int>("localhost", 6380),
						new Tuple<string, int>("localhost", 6381)
					}))
					{
						var sw = Stopwatch.StartNew();

						using (var redisLock = redisLockFactory.Create(lockKey, TimeSpan.FromSeconds(30)))
						{
							sw.Stop();

							Log.InfoFormat("Lock method took {0}ms to return", sw.ElapsedMilliseconds);

							if (redisLock.IsAcquired)
							{
								Log.DebugFormat("Got lock with id {0}, sleeping for a bit", redisLock.LockInfo.LockId);

								Interlocked.Increment(ref locksAcquired);

								// Sleep for long enough for the other threads to give up
								Thread.Sleep(TimeSpan.FromSeconds(2));

								Log.DebugFormat("Lock with id {0} done sleeping", redisLock.LockInfo.LockId);
							}
							else
							{
								Log.Debug("Couldn't get lock, giving up");
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
	}
}
