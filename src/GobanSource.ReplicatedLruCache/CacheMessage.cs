using System.Runtime.CompilerServices;
using GobanSource.Bus.Redis;
namespace GobanSource.ReplicatedLruCache;
/// <summary>
/// Represents a message for cache synchronization across different applications.
/// Used to replicate cache operations (set/remove/clear) between distributed cache instances.
/// </summary>
public class CacheMessage : BaseMessage
{

    /// <summary>
    /// Identifies which cache instance this message belongs to.
    /// Multiple cache instances can exist within the same application.
    /// </summary>
    public string CacheName { get; set; } = null!;

    /// <summary>
    /// The type of cache operation being performed.
    /// </summary>
    public CacheOperation Operation { get; set; }

    /// <summary>
    /// The key of the cache entry being operated on.
    /// </summary>
    public string Key { get; set; } = null!;

    /// <summary>
    /// The value to be cached. Only used for Set operations.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Optional time-to-live for the cache entry. Only used for Set operations.
    /// </summary>
    public TimeSpan? TTL { get; set; }

}

/// <summary>
/// Defines the types of operations that can be performed on the cache.
/// </summary>
public enum CacheOperation
{
    /// <summary>
    /// Sets or updates a cache entry with a key-value pair.
    /// </summary>
    Set,

    /// <summary>
    /// Removes a specific cache entry by key.
    /// </summary>
    Remove,

    /// <summary>
    /// Clears all entries from the cache.
    /// </summary>
    Clear
}