# RedLock.net

An implementation of the [Redlock distributed lock algorithm](http://redis.io/topics/distlock) in C#.

Makes use of the excellent [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) library.

Distributed locks are useful for ensuring only one process is using a particular resource at any given time (even if the processes are running on different machines).

## Usage

Construct a `RedisLockFactory`, passing in a set of independent Redis endpoints. The Redis endpoints should be independent (i.e. not replicated masters/slaves).

You should keep hold of the `RedisLockFactory` and reuse it in your application. Each instance maintains its own connections with the configured Redis instances. Remember to dispose it when your app shuts down.

With the factory, create a `RedisLock` in a using block, making sure to check that that the lock `IsAcquired` inside the block before performing any actions. A lock is acquired if `RedisLock` can set a lock key in more than half of the redis instances.

Internally `RedisLock` has a timer that automatically tries to keep the redis lock key alive. In the worst case of a process crashing without releasing the lock, it will automatically be expired by redis after the specified expiry time.

#### On app init:
```csharp
var endPoints = new[]
{
	new DnsEndPoint("redis1", 6379),
	new DnsEndPoint("redis2", 6379),
	new DnsEndPoint("redis3", 6379)
};
var redisLockFactory = new RedisLockFactory(endPoints);
```

#### When you want to lock on a resource (giving up immediately if the lock is not available):
```csharp
var resource = "the-thing-we-are-locking-on";
var expiry = TimeSpan.FromSeconds(30);

using (var redisLock = redisLockFactory.Create(resource, expiry))
{
	// make sure we got the lock
	if (redisLock.IsAcquired)
	{
		// do stuff
	}
}
// the lock is automatically released at the end of the using block
```

#### When you want to lock on a resource (blocking and retrying every `retry` seconds until the lock is available, or `wait` seconds have passed):
```csharp
var resource = "the-thing-we-are-locking-on";
var expiry = TimeSpan.FromSeconds(30);
var wait = TimeSpan.FromSeconds(10);
var retry = TimeSpan.FromSeconds(1);

// blocks until acquired or 'wait' timeout
using (var redisLock = redisLockFactory.Create(resource, expiry, wait, retry))
{
	// make sure we got the lock
	if (redisLock.IsAcquired)
	{
		// do stuff
	}
}
// the lock is automatically released at the end of the using block
```

#### On app shutdown:
```csharp
redisLockFactory.Dispose();
```
