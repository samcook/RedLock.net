# RedLock.net

An implementation of the [Redlock algorithm](http://redis.io/topics/distlock) in C#. Makes use of the excellent [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis).

## Usage

Construct a `RedisLockFactory` (passing in a set of redis endpoints). You should keep hold of this and reuse it in your application. Each instance of `RedisLockFactory` maintains its own set of connections with Redis. Remember to dispose it when your app shuts down.

With the factory, create a `RedisLock` in a using block, making sure to check that that the lock `IsAcquired` inside the block before performing any actions.

#### On app init:
```csharp
var redisLockFactory = new RedisLockFactory(endPoints);
```

#### When you need a redis lock:
```csharp
var resource = "the-thing-we-are-locking-on";

using (var redisLock = redisLockFactory.Create(resource, TimeSpan.FromSeconds(30)))
{
	// make sure we got the lock
	if (redisLock.IsAcquired)
	{
		// do stuff
	}
}

// the lock is automatically released at the end of the using block
}
```

#### On app shutdown:
```csharp
redisLockFactory.Dispose();
```
