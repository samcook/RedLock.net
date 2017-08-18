using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedLock
{
    public static class IRedisLockExtensions
    {
        public static bool HasError(this IRedisLock redisLock)
        {
            return redisLock.ThrownExceptions.Any();
        }
    }
}
