
namespace GobanSource.ReplicatedLruCache;

public interface ILruCache
{
    void Set(string key, string? value, TimeSpan? ttl = null);
    bool TryGet(string key, out string? value);
    void Remove(string key);
    void Clear();
}