using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedLock
{
    public class AzureRedisCacheEndpoint
    {
        public string Host { get; set; }

        public bool SSL { get; set; }

        public string AccessKey { get; set; }
    }
}
