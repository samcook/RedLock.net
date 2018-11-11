# RedLock.net

[![Build status](https://ci.appveyor.com/api/projects/status/fclfbkdqy905v3xu/branch/master?svg=true)](https://ci.appveyor.com/project/samcook/redlock-net/branch/master) [![GitHub](https://img.shields.io/github/license/samcook/RedLock.net.svg)](LICENSE) [![NuGet](https://img.shields.io/nuget/dt/RedLock.net.svg)](https://www.nuget.org/packages/RedLock.net) [![GitHub release](https://img.shields.io/github/release/samcook/RedLock.net.svg?logo=github&logoColor=cccccc)](https://github.com/samcook/RedLock.net/releases) [![GitHub release](https://img.shields.io/github/release/samcook/RedLock.net/all.svg?label=pre-release&logo=github&logoColor=cccccc)](https://github.com/samcook/RedLock.net/releases)

An implementation of the [Redlock distributed lock algorithm](http://redis.io/topics/distlock) in C#.

Makes use of the excellent [StackExchange.Redis](https://stackexchange.github.io/StackExchange.Redis/) library.

Distributed locks are useful for ensuring only one process is using a particular resource at any given time (even if the processes are running on different machines).

RedLock.net is available using NuGet - search for [RedLock.net](https://www.nuget.org/packages/RedLock.net).

*Note:* RedLock 2.2.0+ requires StackExchange.Redis 2.0+ - if you need to use StackExchange.Redis 1.x please continue to use RedLock.net 2.1.0.

## Usage

Construct a `RedLockFactory`, passing in a set of Redis endpoints. Each endpoint passed to the constructor should be independent (i.e. not replicated masters/slaves). See below for more information on using RedLock.net with replicated instances.

You should keep hold of the `RedLockFactory` and reuse it in your application. Each instance maintains its own connections with the configured Redis instances. Remember to dispose it when your app shuts down.

With the factory, create a `RedLock` in a using block, making sure to check that that the lock `IsAcquired` inside the block before performing any actions. A lock is acquired if `RedLock` can set a lock key in more than half of the redis instances.

Internally `RedLock` has a timer that automatically tries to keep the redis lock key alive. In the worst case of a process crashing without releasing the lock, it will automatically be expired by redis after the specified expiry time.

### On app init:
#### To have RedLock maintain its own redis connections:
```csharp
var endPoints = new List<RedLockEndPoint>
{
	new DnsEndPoint("redis1", 6379),
	new DnsEndPoint("redis2", 6379),
	new DnsEndPoint("redis3", 6379)
};
var redlockFactory = RedLockFactory.Create(endPoints);
```

#### To have RedLock use existing redis connections:
```csharp
var existingConnectionMultiplexer1 = ConnectionMultiplexer.Connect("redis1:6379");
var existingConnectionMultiplexer2 = ConnectionMultiplexer.Connect("redis2:6379");
var existingConnectionMultiplexer3 = ConnectionMultiplexer.Connect("redis3:6379");

var multiplexers = new List<RedLockMultiplexer>
{
	existingConnectionMultiplexer1,
	existingConnectionMultiplexer2,
	existingConnectionMultiplexer3
};
var redlockFactory = RedLockFactory.Create(multiplexers);
```

If you require more detailed configuration for the redis instances you are connecting to (e.g. password, SSL, connection timeout, redis database, key format), you can use the `RedLockEndPoint` or `RedLockMultiplexer` classes.

### When you want to lock on a resource (giving up immediately if the lock is not available):
```csharp
var resource = "the-thing-we-are-locking-on";
var expiry = TimeSpan.FromSeconds(30);

using (var redLock = await redlockFactory.CreateLockAsync(resource, expiry)) // there are also non async Create() methods
{
	// make sure we got the lock
	if (redLock.IsAcquired)
	{
		// do stuff
	}
}
// the lock is automatically released at the end of the using block
```

### When you want to lock on a resource (blocking and retrying every `retry` seconds until the lock is available, or `wait` seconds have passed):
```csharp
var resource = "the-thing-we-are-locking-on";
var expiry = TimeSpan.FromSeconds(30);
var wait = TimeSpan.FromSeconds(10);
var retry = TimeSpan.FromSeconds(1);

// blocks until acquired or 'wait' timeout
using (var redLock = await redlockFactory.CreateLockAsync(resource, expiry, wait, retry)) // there are also non async Create() methods
{
	// make sure we got the lock
	if (redLock.IsAcquired)
	{
		// do stuff
	}
}
// the lock is automatically released at the end of the using block
```

You can also pass a `CancellationToken` to the blocking `CreateLock` methods if you want to be able to cancel the blocking wait.

### On app shutdown:
```csharp
redlockFactory.Dispose();
```

### Azure Redis Cache:
If you are connecting to Azure Redis Cache, use the following configuration settings:
```csharp
var azureEndPoint = new RedLockEndPoint
{
	EndPoint = new DnsEndPoint("YOUR_CACHE.redis.cache.windows.net", 6380),
	Password = "YOUR_ACCESS_KEY",
	Ssl = true
};
```

### Usage with replicated instances:
The Redlock algorithm is designed to be used with multiple independent Redis instances (see http://redis.io/topics/distlock#why-failover-based-implementations-are-not-enough for more detail on why this is the case).

However, since RedLock.net 1.7.3 there is support for replicated master/slave instances. To do so, configure RedLock using the `RedLockEndPoint` class, providing the replicated redis instances with the `EndPoints` property.
Each `RedLockEndPoint` that is passed to the `RedLockFactory` constructor will be treated as a single unit as far as the RedLock algorithm is concerned.

If you have multiple independent sets of replicated Redis instances, you can use those with RedLock.net in the same way you would use multiple non-replicated independent Redis instances:
```csharp
var redlockEndPoints = new List<RedLockEndPoint>
{
	new RedLockEndPoint
	{
		EndPoints =
		{
			new DnsEndPoint("replicatedset1-server1", 6379),
			new DnsEndPoint("replicatedset1-server2", 6379),
			new DnsEndPoint("replicatedset1-server3", 6379)
		}
	},
	new RedLockEndPoint
	{
		EndPoints =
		{
			new DnsEndPoint("replicatedset2-server1", 6379),
			new DnsEndPoint("replicatedset2-server2", 6379),
			new DnsEndPoint("replicatedset2-server3", 6379)
		}
	},
	new RedLockEndPoint
	{
		EndPoint = new DnsEndPoint("independent-server", 6379)
	}
};

var redlockFactory = RedLockFactory.Create(redlockEndPoints);
```
or, if you have existing redis connections:
```csharp
var existingConnectionMultiplexer1 = ConnectionMultiplexer.Connect("replicatedset1-server1:6379,replicatedset1-server2:6379,replicatedset1-server3:6379");
var existingConnectionMultiplexer2 = ConnectionMultiplexer.Connect("replicatedset2-server1:6379,replicatedset2-server2:6379,replicatedset2-server3:6379");
var existingConnectionMultiplexer3 = ConnectionMultiplexer.Connect("independent-server:6379");

var multiplexers = new List<RedLockMultiplexer>
{
	existingConnectionMultiplexer1,
	existingConnectionMultiplexer2,
	existingConnectionMultiplexer3
};
var redlockFactory = RedLockFactory.Create(multiplexers);
```

#### Considerations when using replicated instances
Using replicated instances is not the suggested way to use RedLock, however if your environment is configured that way and you are aware of the potential risks it is possible to configure.

Since all operations that RedLock.net performs in Redis (Lock, Extend and Unlock) require writing, they can only be performed on the master. If your replicated Redis instances are not configured to automatically promote one of the slaves to master in the case of the master becoming inaccessible, the replicated instances will be unable to be used by RedLock until there is manual intervention to reinstate a master.

There is the potential for a master to become inaccessible after acquiring a lock, but before that lock is propogated to the replicated slaves. If one of the slaves is then promoted to master, the lock will not have been acquired within Redis, and there is the possibility that another process could also acquire the lock, resulting in two processes running within the lock section at the same time. Running with multiple independent instances (or multiple independent replicated instances) should mitigate this somewhat, as the RedLock quorum is still required before another process can acquire the lock.

There is the potential for a master to become inaccessible after releasing a lock, but before the lock removal is propogated to the replicated slaves. This will result in the lock continuing to be held until the Redis key expires after the specified expiry time.