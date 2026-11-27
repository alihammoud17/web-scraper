-- Shared token bucket for one site (ADR 0004, ADR 0011). Runs atomically inside Redis, so concurrent
-- workers can't overspend the budget, and uses Redis's own clock, so worker clock drift doesn't matter.
-- KEYS[1] = ratelimit:{authority}
-- ARGV[1] = refill rate in tokens per second, ARGV[2] = bucket capacity (1 = evenly spaced requests)
-- Returns 0 when a token was taken, otherwise the milliseconds to wait before trying again.
local rate = tonumber(ARGV[1])
local capacity = tonumber(ARGV[2])

local time = redis.call('TIME')
local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)

local state = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
local tokens = tonumber(state[1])
local ts = tonumber(state[2])
if tokens == nil or ts == nil then
  tokens = capacity
  ts = now
end

tokens = math.min(capacity, tokens + (now - ts) * rate / 1000)

local wait = 0
if tokens >= 1 then
  tokens = tokens - 1
else
  wait = math.ceil((1 - tokens) * 1000 / rate)
end

redis.call('HSET', KEYS[1], 'tokens', tostring(tokens), 'ts', tostring(now))
-- An idle bucket is full again after capacity / rate seconds; drop the key a minute after that.
redis.call('PEXPIRE', KEYS[1], math.ceil(capacity * 1000 / rate) + 60000)
return wait
