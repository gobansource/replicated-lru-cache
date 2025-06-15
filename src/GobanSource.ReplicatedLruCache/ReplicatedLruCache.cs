
using GobanSource.Bus.Redis;

namespace GobanSource.ReplicatedLruCache;

public class ReplicatedLruCache : IReplicatedLruCache
{
    private readonly string _cacheName;
    private readonly ILruCache _localCache;
    private readonly IRedisSyncBus<CacheMessage> _syncBus;

    public ReplicatedLruCache(
        ILruCache localCache,
        IRedisSyncBus<CacheMessage> syncBus,
        string cacheName
        )
    {
        _localCache = localCache;
        _syncBus = syncBus;
        _cacheName = cacheName;
    }

    public async Task Set(string key, string? value, TimeSpan? ttl = null)
    {
        _localCache.Set(key, value, ttl);
        await _syncBus.PublishAsync(new CacheMessage
        {
            CacheName = _cacheName,
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
            CacheName = _cacheName,
            Operation = CacheOperation.Remove,
            Key = key
        });
    }

    public async Task Clear()
    {
        _localCache.Clear();
        await _syncBus.PublishAsync(new CacheMessage
        {
            CacheName = _cacheName,
            Operation = CacheOperation.Clear
        });
    }
}