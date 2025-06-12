using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace GobanSource.ReplicatedLruCache;
/// <summary>
/// LRU Cache implementation that handles both explicit size limits and memory pressure.
/// This implementation maintains consistency between LRU tracking and MemoryCache entries
/// even when entries are evicted due to memory pressure or TTL expiration.
/// </summary>
public class LruCache : ILruCache
{
    private readonly MemoryCache _cache;
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly ReaderWriterLockSlim _cacheLock = new();
    private readonly int _maxSize;
    private readonly LinkedList<string> _lruList = new();
    private readonly ConcurrentDictionary<string, LinkedListNode<string>> _lruTracker = new();
    private readonly ILogger<LruCache>? _logger;
    private readonly CacheMetrics? _metrics;
    private readonly string _instanceId;

    private enum LruOperation
    {
        Add,
        Remove,
        Clear
    }

    private void UpdateLru(string key, LruOperation operation)
    {
        _lock.EnterWriteLock();
        try
        {
            switch (operation)
            {
                case LruOperation.Add:
                    if (_lruTracker.TryRemove(key, out var existingNode))
                    {
                        _lruList.Remove(existingNode);
                    }

                    var newNode = _lruList.AddFirst(key);
                    _lruTracker[key] = newNode;

                    if (_lruList.Count > _maxSize)
                    {
                        var lruKey = _lruList.Last.Value;
                        _lruList.RemoveLast();
                        _lruTracker.TryRemove(lruKey, out _);
                        _cache.Remove(lruKey);
                    }
                    break;

                case LruOperation.Remove:
                    if (_lruTracker.TryRemove(key, out var node))
                    {
                        _lruList.Remove(node);
                    }
                    break;

                case LruOperation.Clear:
                    _lruList.Clear();
                    _lruTracker.Clear();
                    break;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public LruCache(int maxSize = 1000, ILogger<LruCache>? logger = null, CacheMetrics? metrics = null, string? instanceId = null)
    {
        _maxSize = maxSize;
        _logger = logger;
        _metrics = metrics;
        _instanceId = instanceId ?? Guid.NewGuid().ToString();
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            // Only using MemoryCache for TTL expiration, size management is handled by LRU
            ExpirationScanFrequency = TimeSpan.FromMinutes(5)
        });
    }

    /// <summary>
    /// Sets a value in the cache with optional TTL.
    /// The entry will be evicted when either:
    /// 1. LRU capacity is reached (controlled by maxSize)
    /// 2. TTL expires (if specified)
    /// 3. System memory pressure causes MemoryCache to trim entries
    /// In all cases, LRU tracking state will be kept consistent via eviction callbacks.
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <param name="value">The value to cache</param>
    /// <param name="ttl">Optional time-to-live for the entry</param>
    public void Set(string key, string? value, TimeSpan? ttl = null)
    {
        _cacheLock.EnterWriteLock();
        try
        {
            var options = new MemoryCacheEntryOptions();
            options.RegisterPostEvictionCallback(OnEntryEvicted);

            if (ttl.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = ttl;
            }

            UpdateLru(key, LruOperation.Add);
            _cache.Set(key, value, options);
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    public bool TryGet(string key, out string? value)
    {
        _cacheLock.EnterUpgradeableReadLock();
        try
        {
            if (_cache.TryGetValue(key, out string? cachedValue))
            {
                value = cachedValue;
                UpdateLru(key, LruOperation.Add);
                _metrics?.RecordHit(_instanceId);
                return true;
            }
        }
        finally
        {
            _cacheLock.ExitUpgradeableReadLock();
        }

        value = default;
        _metrics?.RecordMiss(_instanceId);
        return false;
    }

    public void Remove(string key)
    {
        _cacheLock.EnterWriteLock();
        try
        {
            _cache.Remove(key);
            UpdateLru(key, LruOperation.Remove);
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    public void Clear()
    {
        _cache.Clear();
        UpdateLru(string.Empty, LruOperation.Clear);
    }

    /// <summary>
    /// Callback invoked when an entry is evicted from MemoryCache.
    /// This keeps LRU tracking state consistent with MemoryCache state.
    /// Called in cases of:
    /// - TTL expiration
    /// - Memory pressure eviction
    /// - Explicit removal
    /// - Cache clearing
    /// </summary>
    private void OnEntryEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        // Only handle evictions we didn't explicitly trigger
        if (reason != EvictionReason.Removed)
        {
            _logger?.LogDebug("[LruCache] Evicted key: {Key}, Reason: {Reason}", key, reason);
            UpdateLru((string)key, LruOperation.Remove);
        }
    }

    public int Count => _cache.Count;
}