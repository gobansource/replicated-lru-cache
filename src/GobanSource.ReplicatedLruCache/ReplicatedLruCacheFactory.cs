
using GobanSource.Bus.Redis;
using System.Reflection;

namespace GobanSource.ReplicatedLruCache;

public class ReplicatedLruCacheFactory : IReplicatedLruCacheFactory
{

    public T Create<T>(string instanceId, ILruCache lruCache, IRedisSyncBus<CacheMessage> syncBus) where T : class, IReplicatedLruCache
    {

        var cache = new ReplicatedLruCache(lruCache, syncBus, instanceId);

        return ReplicatedLruCacheProxy<T>.Create(cache);
    }
}

public class ReplicatedLruCacheProxy<T> : DispatchProxy
    where T : class, IReplicatedLruCache
{
    private ReplicatedLruCache _cache;

    public static T Create(ReplicatedLruCache cache)
    {
        var proxy = Create<T, ReplicatedLruCacheProxy<T>>() as ReplicatedLruCacheProxy<T>;
        proxy._cache = cache;
        return proxy as T;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        return targetMethod?.Invoke(_cache, args);
    }
}