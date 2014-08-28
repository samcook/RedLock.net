# RedLock.net

An implementation of the [Redlock algorithm](http://redis.io/topics/distlock) in C#. Makes use of the excellent [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis).

## Usage

Construct a RedisLockFactory (passing in a set of redis endpoints). With the factory, create a RedisLock in a using block, making sure to check that you have the lock inside the block before performing any actions.

```
using (var redisLockFactory = new RedisLockFactory(endPoints))
{
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
