using System.Reflection;

namespace GobanSource.ReplicatedLruCache;

public class ReplicatedLruCacheProxy<T> : DispatchProxy where T : class, IReplicatedLruCache
{
    private ReplicatedLruCache _cache = null!;

    public static T Create(ReplicatedLruCache cache)
    {
        var proxy = Create<T, ReplicatedLruCacheProxy<T>>() as ReplicatedLruCacheProxy<T>;
        proxy!._cache = cache;
        return proxy as T;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        return targetMethod?.Invoke(_cache, args);
    }
}