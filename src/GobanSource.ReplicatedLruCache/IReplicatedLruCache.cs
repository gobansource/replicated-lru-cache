
namespace GobanSource.ReplicatedLruCache;

public interface IReplicatedLruCache
{
    Task Set(string key, string? value, TimeSpan? ttl = null);
    bool TryGet(string key, out string? value);
    Task Remove(string key);
    Task Clear();
}