
using GobanSource.Bus.Redis;

namespace GobanSource.ReplicatedLruCache;

public class ReplicatedLruCache : IReplicatedLruCache
{
    private readonly string _cacheInstanceId;
    private readonly ILruCache _localCache;
    private readonly IRedisSyncBus<CacheMessage> _syncBus;

    public ReplicatedLruCache(
        ILruCache localCache,
        IRedisSyncBus<CacheMessage> syncBus,
        string cacheInstanceId
        )
    {
        _localCache = localCache;
        _syncBus = syncBus;
        _cacheInstanceId = cacheInstanceId;
    }

    public async Task Set(string key, string? value, TimeSpan? ttl = null)
    {
        _localCache.Set(key, value, ttl);
        await _syncBus.PublishAsync(new CacheMessage
        {
            CacheInstanceId = _cacheInstanceId,
            Operation = CacheOperation.Set,
            Key = key,
            Value = value,
            TTL = ttl
        });
    }

    public bool TryGet(string key, out string? value) =>
        _localCache.TryGet(key, out value);

    public async Task Remove(string key)
    {
        _localCache.Remove(key);
        await _syncBus.PublishAsync(new CacheMessage
        {
            CacheInstanceId = _cacheInstanceId,
            Operation = CacheOperation.Remove,
            Key = key
        });
    }

    public async Task Clear()
    {
        _localCache.Clear();
        await _syncBus.PublishAsync(new CacheMessage
        {
            CacheInstanceId = _cacheInstanceId,
            Operation = CacheOperation.Clear
        });
    }
}