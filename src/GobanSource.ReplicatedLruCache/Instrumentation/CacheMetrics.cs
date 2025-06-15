using System.Diagnostics.Metrics;

namespace GobanSource.ReplicatedLruCache;

public class CacheMetrics
{
    private static readonly Meter Meter = new("GobanSource.Cache.Replicated", "1.0.0");

    // Changed to static
    private static readonly Counter<long> _cacheHits = Meter.CreateCounter<long>("cache.hits", description: "Count of cache hits");
    private static readonly Counter<long> _cacheMisses = Meter.CreateCounter<long>("cache.misses", description: "Count of cache misses");

    public static string MeterName => Meter.Name;

    public void RecordHit(string cacheName)
    {
        _cacheHits.Add(1, new KeyValuePair<string, object?>("cache_instance", cacheName));
    }

    public void RecordMiss(string cacheName)
    {
        _cacheMisses.Add(1, new KeyValuePair<string, object?>("cache_instance", cacheName));
    }
}