using log4net.Config;
using NUnit.Framework;
using RedLock.Logging;
using RedLock.Logging.Log4Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedLock.Tests
{
    public class AzureRedisCacheEndpointTest
    {

        private const string AzureRedisCacheHost = "YOUR_CACHE.redis.cache.windows.net";
        private const string AzureRedisCacheAccessKey = "YOUR_ACCESS_KEY";

        [Test]
        public void TestSSLWithSingleRedisLock()
        {
            var azureRedisEndpoint = new AzureRedisCacheEndpoint
            {
                Host = AzureRedisCacheHost,
                AccessKey = AzureRedisCacheAccessKey,
                SSL = true
            };

            using (var redisLockFactory = new RedisLockFactory(azureRedisEndpoint))
            {
                var resource = String.Format("testredislock-{0}", Guid.NewGuid());

                using (var redisLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(30)))
                {
                    Assert.True(redisLock.IsAcquired);
                }
            }
        }

        [Test]
        public void TestNonSSLWithSingleRedisLock()
        {
            var azureRedisEndpoint = new AzureRedisCacheEndpoint
            {
                Host = AzureRedisCacheHost,
                AccessKey = AzureRedisCacheAccessKey,
                SSL = false
            };

            using (var redisLockFactory = new RedisLockFactory(azureRedisEndpoint))
            {
                var resource = String.Format("testredislock-{0}", Guid.NewGuid());

                using (var redisLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(30)))
                {
                    Assert.True(redisLock.IsAcquired);
                }
            }
        }
    }
}
