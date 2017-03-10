local currentVal = redis.call('get', KEYS[1])
if (currentVal == false) then
	return redis.call('set', KEYS[1], ARGV[1], 'PX', ARGV[2]) and 1 or 0
elseif (currentVal == ARGV[1]) then
	return redis.call('pexpire', KEYS[1], ARGV[2])
else
	return -1
end