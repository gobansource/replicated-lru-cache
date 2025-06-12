using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;

namespace GobanSource.ReplicatedLruCache.Tests.Benchmarks
{
    [MemoryDiagnoser]
    [HtmlExporter]
    public class CacheBenchmarks
    {
        private LruCache _lruCache = null!;
        private IMemoryCache _memoryCache = null!;
        private readonly string[] _keys;
        private readonly string[] _values;
        private readonly MemoryCacheEntryOptions _cacheOptions;

        public CacheBenchmarks()
        {
            _keys = Enumerable.Range(0, 10000).Select(i => $"key{i}").ToArray();
            _values = Enumerable.Range(0, 10000).Select(i => $"value{i}").ToArray();
            _cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(30))
                .SetSize(1); // Each entry counts as 1 size unit
        }

        [GlobalSetup]
        public void Setup()
        {
            _lruCache = new LruCache(1000);
            _memoryCache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 1000 // Match LruCache capacity
            });

            // Prefill some data
            for (int i = 0; i < 500; i++)
            {
                _lruCache.Set(_keys[i], _values[i]);
                _memoryCache.Set(_keys[i], _values[i], _cacheOptions);
            }
        }

        [Benchmark(Baseline = true, Description = "LRU Set")]
        public void LruCache_Set()
        {
            _lruCache.Set("newKey", "newValue");
        }

        [Benchmark(Description = "MemoryCache Set")]
        public void MemoryCache_Set()
        {
            _memoryCache.Set("newKey", "newValue", _cacheOptions);
        }

        [Benchmark(Description = "LRU Get Existing")]
        public void LruCache_GetExisting()
        {
            _lruCache.TryGet(_keys[0], out _);
        }

        [Benchmark(Description = "MemoryCache Get Existing")]
        public void MemoryCache_GetExisting()
        {
            _memoryCache.TryGetValue(_keys[0], out _);
        }

        [Benchmark(Description = "LRU Get Missing")]
        public void LruCache_GetMissing()
        {
            _lruCache.TryGet("nonexistent", out _);
        }

        [Benchmark(Description = "MemoryCache Get Missing")]
        public void MemoryCache_GetMissing()
        {
            _memoryCache.TryGetValue("nonexistent", out _);
        }

        [Benchmark(Description = "LRU Eviction")]
        public void LruCache_Eviction()
        {
            for (int i = 0; i < 1100; i++)
            {
                _lruCache.Set(_keys[i], _values[i]);
            }
        }

        [Benchmark(Description = "MemoryCache Eviction")]
        public void MemoryCache_Eviction()
        {
            for (int i = 0; i < 1100; i++)
            {
                _memoryCache.Set(_keys[i], _values[i], _cacheOptions);
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            (_memoryCache as IDisposable)?.Dispose();
        }
    }
}