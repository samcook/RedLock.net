@echo off

setlocal

if not defined REDIS_PATH (
	set REDIS_PATH=..\..\packages\redis-64.3.0.500\tools
)

echo %REDIS_PATH%\redis-server.exe

start /D 6379 %REDIS_PATH%\redis-server.exe redis.conf

endlocal