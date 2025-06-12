
using GobanSource.Bus.Redis;

namespace GobanSource.ReplicatedLruCache;

public interface IReplicatedLruCacheFactory
{
    T Create<T>(string instanceId, ILruCache lruCache, IRedisSyncBus<CacheMessage> syncBus) where T : class, IReplicatedLruCache;
}