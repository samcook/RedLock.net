local currentVal = redis.call('get', KEYS[1])
if (currentVal == false) then
	return 1;
elseif (currentVal == ARGV[1]) then
	return 1;
else
	return -1
end