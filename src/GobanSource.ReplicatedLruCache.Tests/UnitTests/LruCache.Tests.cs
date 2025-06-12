namespace GobanSource.ReplicatedLruCache.Tests.UnitTests;

[TestClass]
public class LruCacheTests
{
    private LruCache _cache = null!;
    private const int DefaultMaxSize = 1000;

    [TestInitialize]
    public void Setup()
    {
        _cache = new LruCache(DefaultMaxSize);
    }

    [TestMethod]
    public void Set_WhenKeyDoesNotExist_AddsToCache()
    {
        // Act
        _cache.Set("key1", "value1");

        // Assert
        Assert.IsTrue(_cache.TryGet("key1", out var value));
        Assert.AreEqual("value1", value);
        Assert.AreEqual(1, _cache.Count);
    }

    [TestMethod]
    public void Set_WhenKeyExists_UpdatesValue()
    {
        // Arrange
        _cache.Set("key1", "value1");

        // Act
        _cache.Set("key1", "value2");

        // Assert
        Assert.IsTrue(_cache.TryGet("key1", out var value));
        Assert.AreEqual("value2", value);
        Assert.AreEqual(1, _cache.Count);
    }

    [TestMethod]
    public void Set_WhenCacheIsFull_EvictsLeastRecentlyUsed()
    {
        // Arrange
        for (int i = 0; i < DefaultMaxSize; i++)
        {
            _cache.Set($"key{i}", $"value{i}");
        }

        // Act - This should evict key1
        _cache.Set("keyabc", "value114");

        // Assert
        Assert.IsFalse(_cache.TryGet("key0", out _));
        Assert.IsTrue(_cache.TryGet("key1", out _));
        Assert.IsTrue(_cache.TryGet("key2", out _));
        Assert.IsTrue(_cache.TryGet("keyabc", out _));
        Assert.AreEqual(DefaultMaxSize, _cache.Count);
    }

    [TestMethod]
    public void TryGet_WhenKeyExists_UpdatesLruOrder()
    {
        // Arrange
        for (int i = 0; i < DefaultMaxSize; i++)
        {
            _cache.Set($"key{i}", $"value{i}");
        }

        // Act - Access key1, making it most recently used
        _cache.TryGet("key0", out _);
        // Add new item - should evict key2 instead of key1
        _cache.Set("keyabc", "valueabc");

        // Assert
        Assert.IsTrue(_cache.TryGet("key0", out _));
        Assert.IsFalse(_cache.TryGet("key1", out _));
        Assert.IsTrue(_cache.TryGet("key2", out _));
        Assert.IsTrue(_cache.TryGet("keyabc", out _));
    }

    [TestMethod]
    public void TryGet_WhenKeyDoesNotExist_ReturnsFalse()
    {
        // Act & Assert
        Assert.IsFalse(_cache.TryGet("nonexistent", out var value));
        Assert.AreEqual(default, value);
    }

    [TestMethod]
    public void Remove_WhenKeyExists_RemovesFromCache()
    {
        // Arrange
        _cache.Set("key1", "value1");

        // Act
        _cache.Remove("key1");

        // Assert
        Assert.IsFalse(_cache.TryGet("key1", out _));
        Assert.AreEqual(0, _cache.Count);
    }

    [TestMethod]
    public void Remove_WhenKeyDoesNotExist_DoesNothing()
    {
        // Arrange
        _cache.Set("key1", "value1");

        // Act
        _cache.Remove("nonexistent");

        // Assert
        Assert.AreEqual(1, _cache.Count);
        Assert.IsTrue(_cache.TryGet("key1", out _));
    }

    [TestMethod]
    public void Clear_RemovesAllItems()
    {
        // Arrange
        _cache.Set("key1", "value1");
        _cache.Set("key2", "value2");

        // Act
        _cache.Clear();

        // Assert
        Assert.AreEqual(0, _cache.Count);
        Assert.IsFalse(_cache.TryGet("key1", out _));
        Assert.IsFalse(_cache.TryGet("key2", out _));
    }

    [TestMethod]
    public void Set_WithTtl_ExpiresCacheEntry()
    {
        // Arrange
        _cache.Set("key1", "value1", TimeSpan.FromMilliseconds(100));

        // Act
        Task.Delay(200).Wait(); // Wait for TTL to expire

        // Assert
        Assert.IsFalse(_cache.TryGet("key1", out _));
    }

    [TestMethod]
    public void Set_ConcurrentOperations_MaintainsConsistency()
    {
        // Arrange
        var tasks = new Task[DefaultMaxSize];

        // Act
        for (int i = 0; i < DefaultMaxSize; i++)
        {
            var key = $"key{i}";
            var value = $"value{i}";
            tasks[i] = Task.Run(() => _cache.Set(key, value));
        }
        Task.WaitAll(tasks);

        // Assert
        Assert.AreEqual(DefaultMaxSize, _cache.Count);
    }

    [TestMethod]
    public void ConcurrentOperations_WithHalfFullCache_MaintainsConsistency()
    {
        // Arrange
        var halfSize = DefaultMaxSize / 2;
        for (int i = 0; i < halfSize; i++)
        {
            _cache.Set($"initial_key{i}", $"initial_value{i}");
        }

        var operations = 10000;
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < operations; i++)
        {
            var index = Random.Shared.Next(0, halfSize);
            // Mix of gets and sets
            if (i % 2 == 0)
            {
                tasks.Add(Task.Run(() => _cache.Set($"initial_key{Random.Shared.Next(0, halfSize)}", $"value{Random.Shared.Next(0, halfSize)}")));
            }
            else
            {
                var keyToGet = $"initial_key{Random.Shared.Next(0, halfSize)}";
                tasks.Add(Task.Run(() => _cache.TryGet(keyToGet, out _)));
            }
        }
        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.AreEqual(halfSize, _cache.Count);
    }

    [TestMethod]
    public void ConcurrentOperations_WithFullCache_MaintainsConsistency()
    {
        // Arrange
        for (int i = 0; i < DefaultMaxSize; i++)
        {
            _cache.Set($"initial_key{i}", $"initial_value{i}");
        }
        Assert.AreEqual(DefaultMaxSize, _cache.Count); // Verify cache is full

        var operations = 10000;
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < operations; i++)
        {
            var index = i;
            // Mix of gets and sets
            if (i % 2 == 0)
            {
                tasks.Add(Task.Run(() => _cache.Set($"key{index}", $"value{index}")));
            }
            else
            {
                var keyToGet = $"initial_key{index % DefaultMaxSize}";
                tasks.Add(Task.Run(() => _cache.TryGet(keyToGet, out _)));
            }
        }
        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.AreEqual(DefaultMaxSize, _cache.Count);
    }

    [TestMethod]
    public void MassiveConcurrentOperations_MaintainsKeyValueIntegrity()
    {
        // Arrange
        var operations = 100000;
        var keyRange = DefaultMaxSize / 2; // Work with half the cache size to ensure some overwrites
        var tasks = new List<Task>();
        var random = new Random();

        // Initial population
        for (int i = 0; i < keyRange; i++)
        {
            _cache.Set($"key{i}", $"initial_value{i}");
        }

        // Act - Mix of gets and sets with value verification
        for (int i = 0; i < operations; i++)
        {
            var keyIndex = random.Next(0, keyRange);
            var key = $"key{keyIndex}";

            if (i % 2 == 0)
            {
                var newValue = $"value{keyIndex}_{i}";
                tasks.Add(Task.Run(() => _cache.Set(key, newValue)));
            }
            else
            {
                tasks.Add(Task.Run(() =>
                {
                    if (_cache.TryGet(key, out var value))
                    {
                        Assert.IsNotNull(value);
                        // Verify value format is valid
                        Assert.IsTrue(
                            value.StartsWith($"initial_value{keyIndex}") ||
                            value.StartsWith($"value{keyIndex}_"),
                            $"Invalid value format: {value} for key: {key}");
                    }
                }));
            }
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - Verify final state
        Assert.IsTrue(_cache.Count <= DefaultMaxSize);

        // Verify all remaining entries have valid value formats
        for (int i = 0; i < keyRange; i++)
        {
            if (_cache.TryGet($"key{i}", out var finalValue))
            {
                Assert.IsNotNull(finalValue);
                Assert.IsTrue(
                    finalValue.StartsWith($"initial_value{i}") ||
                    finalValue.StartsWith($"value{i}_"),
                    $"Invalid final value format: {finalValue} for key: key{i}");
            }
        }
    }
}
