using Castle.DynamicProxy;
using GobanSource.Bus.Redis;


namespace GobanSource.ReplicatedLruCache;

public class ReplicatedLruCacheFactory : IReplicatedLruCacheFactory
{
    private readonly ProxyGenerator _proxyGenerator = new();

    public T Create<T>(string instanceId, ILruCache lruCache, IRedisSyncBus<CacheMessage> syncBus) where T : class, IReplicatedLruCache
    {

        var cache = new ReplicatedLruCache(lruCache, syncBus, instanceId);

        return _proxyGenerator.CreateInterfaceProxyWithoutTarget<T>(
            new CacheInterceptor(cache)
        );
    }
}

public class CacheInterceptor : IInterceptor
{
    private readonly ReplicatedLruCache _lruCache;

    public CacheInterceptor(ReplicatedLruCache lruCache)
    {
        _lruCache = lruCache;
    }

    public void Intercept(IInvocation invocation)
    {
        invocation.ReturnValue = invocation.Method.Invoke(_lruCache, invocation.Arguments);
    }
}