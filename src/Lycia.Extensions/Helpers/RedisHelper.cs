using StackExchange.Redis;

namespace Lycia.Extensions.Helpers;

public static class RedisHelper
{
    private static readonly string AtomicHashSetIfEqualScript = @"
local current = redis.call('hget', KEYS[1], ARGV[1])
if (not current and ARGV[2] == '') or (current == ARGV[2]) then
  redis.call('hset', KEYS[1], ARGV[1], ARGV[3])
  return 1
else
  return 0
end";

    public static async Task<bool> HashSetFieldIfEqualAsync(
        IDatabase redisDb,
        string hashKey,
        string field,
        string? expectedOldValue,
        string newValue)
    {
        // Empty string is special marker for non-existing
        var oldVal = expectedOldValue ?? "";
        var result = (int)(await redisDb.ScriptEvaluateAsync(
            AtomicHashSetIfEqualScript,
            [hashKey],
            [field, oldVal, newValue]
        ));
        return result == 1;
    }
}