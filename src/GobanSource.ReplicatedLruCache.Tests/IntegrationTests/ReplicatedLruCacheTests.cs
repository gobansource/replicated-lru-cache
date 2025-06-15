using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using GobanSource.Bus.Redis;
using StackExchange.Redis;

namespace GobanSource.ReplicatedLruCache.Tests.IntegrationTests;

[TestClass]
public class ReplicatedLruCacheTests
{
    private MessageSyncHostedService<CacheMessage> _service1 = null!;
    private MessageSyncHostedService<CacheMessage> _service2 = null!;
    private MessageSyncHostedService<CacheMessage> _service3 = null!;
    private IReplicatedLruCache _replicatedLruCache1 = null!;
    private IReplicatedLruCache _replicatedLruCache2 = null!;
    private IReplicatedLruCache _replicatedLruCache3 = null!;
    private IServiceProvider _serviceProvider1 = null!;
    private IServiceProvider _serviceProvider2 = null!;
    private IServiceProvider _serviceProvider3 = null!;
    private IRedisSyncBus<CacheMessage> _syncBus1 = null!;
    private IRedisSyncBus<CacheMessage> _syncBus2 = null!;
    private IRedisSyncBus<CacheMessage> _syncBus3 = null!;
    private string _appId = null!;
    private const string CacheName = "test-cache";
    private const int CacheSize = 10000;
    private string _channelPrefix = null!;
    [TestInitialize]
    public void Setup()
    {
        _appId = Guid.NewGuid().ToString();
        _channelPrefix = "test-cache-sync";

        // Create first instance
        var services1 = new ServiceCollection();
        services1.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services1.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReplicatedLruCache:AppId"] = _appId,
                ["ReplicatedLruCache:RedisSyncBus:ChannelPrefix"] = _channelPrefix,
                ["ReplicatedLruCache:RedisSyncBus:ConnectionString"] = "localhost:6379"
            }).Build());
        var mux1 = ConnectionMultiplexer.Connect("localhost:6379");
        services1.AddSingleton<IConnectionMultiplexer>(mux1);
        services1.AddReplicatedLruCache<IReplicatedLruCache>(CacheSize, CacheName);

        _serviceProvider1 = services1.BuildServiceProvider();
        _service1 = _serviceProvider1.GetServices<IHostedService>().OfType<MessageSyncHostedService<CacheMessage>>().First();
        _syncBus1 = _serviceProvider1.GetRequiredService<IRedisSyncBus<CacheMessage>>();
        _replicatedLruCache1 = _serviceProvider1.GetRequiredService<IReplicatedLruCache>();

        // Create second instance
        var services2 = new ServiceCollection();
        services2.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services2.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReplicatedLruCache:AppId"] = _appId,
                ["ReplicatedLruCache:RedisSyncBus:ChannelPrefix"] = _channelPrefix,
                ["ReplicatedLruCache:RedisSyncBus:ConnectionString"] = "localhost:6379"
            }).Build());
        var mux2 = ConnectionMultiplexer.Connect("localhost:6379");
        services2.AddSingleton<IConnectionMultiplexer>(mux2);
        services2.AddReplicatedLruCache<IReplicatedLruCache>(CacheSize, CacheName);

        _serviceProvider2 = services2.BuildServiceProvider();
        _service2 = _serviceProvider2.GetServices<IHostedService>().OfType<MessageSyncHostedService<CacheMessage>>().First();
        _syncBus2 = _serviceProvider2.GetRequiredService<IRedisSyncBus<CacheMessage>>();
        _replicatedLruCache2 = _serviceProvider2.GetRequiredService<IReplicatedLruCache>();

        // Create third instance
        var services3 = new ServiceCollection();
        services3.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services3.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReplicatedLruCache:AppId"] = _appId,
                ["ReplicatedLruCache:RedisSyncBus:ChannelPrefix"] = _channelPrefix,
                ["ReplicatedLruCache:RedisSyncBus:ConnectionString"] = "localhost:6379"
            }).Build());
        var mux3 = ConnectionMultiplexer.Connect("localhost:6379");
        services3.AddSingleton<IConnectionMultiplexer>(mux3);
        services3.AddReplicatedLruCache<IReplicatedLruCache>(CacheSize, CacheName);

        _serviceProvider3 = services3.BuildServiceProvider();
        _service3 = _serviceProvider3.GetServices<IHostedService>().OfType<MessageSyncHostedService<CacheMessage>>().First();
        _syncBus3 = _serviceProvider3.GetRequiredService<IRedisSyncBus<CacheMessage>>();
        _replicatedLruCache3 = _serviceProvider3.GetRequiredService<IReplicatedLruCache>();

        // Start all services
        Task.Run(() => _service1.StartAsync(default)).GetAwaiter().GetResult();
        Task.Run(() => _service2.StartAsync(default)).GetAwaiter().GetResult();
        Task.Run(() => _service3.StartAsync(default)).GetAwaiter().GetResult();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_service1 != null)
            await _service1.StopAsync(default);
        if (_service2 != null)
            await _service2.StopAsync(default);
        if (_service3 != null)
            await _service3.StopAsync(default);

        if (_syncBus1 is IAsyncDisposable disposable1)
            await disposable1.DisposeAsync();
        if (_syncBus2 is IAsyncDisposable disposable2)
            await disposable2.DisposeAsync();
        if (_syncBus3 is IAsyncDisposable disposable3)
            await disposable3.DisposeAsync();

        if (_serviceProvider1 is IAsyncDisposable asyncDisposable1)
            await asyncDisposable1.DisposeAsync();
        if (_serviceProvider2 is IAsyncDisposable asyncDisposable2)
            await asyncDisposable2.DisposeAsync();
        if (_serviceProvider3 is IAsyncDisposable asyncDisposable3)
            await asyncDisposable3.DisposeAsync();
    }

    [TestMethod]
    public async Task Set_ShouldReplicateToOtherInstances()
    {
        // Arrange
        var key = "key1";
        var value = "value1";
        var ttl = TimeSpan.FromMinutes(5);

        // Act - Set in first instance
        await _replicatedLruCache1.Set(key, value, ttl);

        // Wait for replication with timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out _) || !_replicatedLruCache3.TryGet(key, out _))
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - Other instances should have the value
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out var replicatedValue2));
        Assert.AreEqual(value, replicatedValue2);
        Assert.IsTrue(_replicatedLruCache3.TryGet(key, out var replicatedValue3));
        Assert.AreEqual(value, replicatedValue3);
    }

    [TestMethod]
    public async Task Remove_ShouldReplicateToOtherInstances()
    {
        // Arrange
        var key = "key1";
        var value = "value1";
        await _replicatedLruCache1.Set(key, value);
        await _replicatedLruCache2.Set(key, value);

        await _replicatedLruCache3.Set(key, value);
        // Verify all caches have the value
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out _));
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out _));
        Assert.IsTrue(_replicatedLruCache3.TryGet(key, out _));

        // wait for avoid race condition on redis server
        await Task.Delay(100);

        // Act - Remove from first instance
        await _replicatedLruCache1.Remove(key);

        // Wait for replication with timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (_replicatedLruCache2.TryGet(key, out _) || _replicatedLruCache3.TryGet(key, out _))
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - All instances should not have the value
        Assert.IsFalse(_replicatedLruCache1.TryGet(key, out _));
        Assert.IsFalse(_replicatedLruCache2.TryGet(key, out _));
        Assert.IsFalse(_replicatedLruCache3.TryGet(key, out _));
    }

    [TestMethod]
    public async Task Clear_ShouldReplicateToOtherInstances()
    {
        // Arrange
        await _replicatedLruCache1.Set("key1", "value1");
        await _replicatedLruCache1.Set("key2", "value2");
        await _replicatedLruCache2.Set("key1", "value1");
        await _replicatedLruCache2.Set("key2", "value2");
        await _replicatedLruCache3.Set("key1", "value1");
        await _replicatedLruCache3.Set("key2", "value2");
        await Task.Delay(100);
        // Verify all caches have the values
        Assert.IsTrue(_replicatedLruCache1.TryGet("key1", out _));
        Assert.IsTrue(_replicatedLruCache1.TryGet("key2", out _));
        Assert.IsTrue(_replicatedLruCache2.TryGet("key1", out _));
        Assert.IsTrue(_replicatedLruCache2.TryGet("key2", out _));
        Assert.IsTrue(_replicatedLruCache3.TryGet("key1", out _));
        Assert.IsTrue(_replicatedLruCache3.TryGet("key2", out _));

        // Act - Clear first instance
        await _replicatedLruCache1.Clear();

        // Wait for replication with timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (_replicatedLruCache2.TryGet("key1", out _) || _replicatedLruCache2.TryGet("key2", out _) ||
                   _replicatedLruCache3.TryGet("key1", out _) || _replicatedLruCache3.TryGet("key2", out _))
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - All instances should be empty
        Assert.IsFalse(_replicatedLruCache1.TryGet("key1", out _));
        Assert.IsFalse(_replicatedLruCache1.TryGet("key2", out _));
        Assert.IsFalse(_replicatedLruCache2.TryGet("key1", out _));
        Assert.IsFalse(_replicatedLruCache2.TryGet("key2", out _));
        Assert.IsFalse(_replicatedLruCache3.TryGet("key1", out _));
        Assert.IsFalse(_replicatedLruCache3.TryGet("key2", out _));
    }

    [TestMethod]
    public async Task Set_ShouldPropagateUpdatesAcrossAllInstancesInSequence()
    {
        // Arrange
        var key = "key1";
        var value1 = "value1";
        var value2 = "value2";
        var value3 = "value3";

        // Set initial value in first cache
        await _replicatedLruCache1.Set(key, value1);

        // Wait for replication
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v2) || v2 != value1 ||
                   !_replicatedLruCache3.TryGet(key, out var v3) || v3 != value1)
            {
                cts1.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts1.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for initial replication");
        }

        // Act - Update value in second instance
        await _replicatedLruCache2.Set(key, value2);

        // Wait for replication
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache1.TryGet(key, out var v1) || v1 != value2 ||
                   !_replicatedLruCache3.TryGet(key, out var v3) || v3 != value2)
            {
                cts2.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts2.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Update value in third instance
        await _replicatedLruCache3.Set(key, value3);

        // Wait for replication
        using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache1.TryGet(key, out var v1) || v1 != value3 ||
                   !_replicatedLruCache2.TryGet(key, out var v2) || v2 != value3)
            {
                cts3.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts3.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - All instances should have the final value
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out var cache1Value));
        Assert.AreEqual(value3, cache1Value);
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out var cache2Value));
        Assert.AreEqual(value3, cache2Value);
        Assert.IsTrue(_replicatedLruCache3.TryGet(key, out var cache3Value));
        Assert.AreEqual(value3, cache3Value);
    }

    [TestMethod]
    public async Task LruEviction_WhenMaxSizeReached_ShouldEvictLeastRecentlyUsedItemLocally()
    {
        // Arrange - Fill cache to max size
        var items = new List<(string key, string value)>();
        for (int i = 0; i < CacheSize; i++)
        {
            items.Add(($"key{i}", $"value{i}"));
            await _replicatedLruCache1.Set(items[i].key, items[i].value);
        }

        // Wait for replication of last item
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(items[CacheSize - 1].key, out _))
            {
                cts1.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts1.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for initial replication");
        }

        // Access first item in cache1 to make it most recently used (only in cache1)
        Assert.IsTrue(_replicatedLruCache1.TryGet(items[0].key, out _));

        // Add new item to trigger eviction
        var newKey = "newKey";
        var newValue = "newValue";
        await _replicatedLruCache1.Set(newKey, newValue);

        // Wait for replication
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(newKey, out _))
            {
                cts2.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts2.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - In cache1: first item exists (was accessed), second item evicted (least recently used)
        Assert.IsTrue(_replicatedLruCache1.TryGet(items[0].key, out var resultValue1));
        Assert.AreEqual(items[0].value, resultValue1);
        Assert.IsFalse(_replicatedLruCache1.TryGet(items[1].key, out _));
        Assert.IsTrue(_replicatedLruCache1.TryGet(newKey, out var resultValue2));
        Assert.AreEqual(newValue, resultValue2);

        // In cache2: LRU order is different, might have evicted a different item
        // We only verify the new item exists, as that's the only guaranteed behavior
        Assert.IsTrue(_replicatedLruCache2.TryGet(newKey, out var cache2Value));
        Assert.AreEqual(newValue, cache2Value);
    }

    [TestMethod]
    public async Task LruOrder_ShouldUpdateOnGetLocally()
    {
        // Arrange - Fill cache to max size
        var items = new List<(string key, string value)>();
        for (int i = 0; i < CacheSize; i++)
        {
            items.Add(($"key{i}", $"value{i}"));
            await _replicatedLruCache1.Set(items[i].key, items[i].value);
        }

        // Wait for replication of last item
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(items[CacheSize - 1].key, out _))
            {
                cts1.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts1.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Access first item in cache1 to make it most recently used (only in cache1)
        Assert.IsTrue(_replicatedLruCache1.TryGet(items[0].key, out _));

        // Add new item to trigger eviction
        var newKey = "newKey";
        var newValue = "newValue";
        await _replicatedLruCache1.Set(newKey, newValue);

        // Wait for replication
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(newKey, out _))
            {
                cts2.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts2.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - In cache1: first item exists (was accessed), second item evicted (least recently used)
        Assert.IsTrue(_replicatedLruCache1.TryGet(items[0].key, out _));
        Assert.IsFalse(_replicatedLruCache1.TryGet(items[1].key, out _));
        Assert.IsTrue(_replicatedLruCache1.TryGet(newKey, out _));

        // In cache2: LRU order is different, might have evicted a different item
        // We only verify the new item exists, as that's the only guaranteed behavior
        Assert.IsTrue(_replicatedLruCache2.TryGet(newKey, out _));
    }

    [TestMethod]
    public async Task LruOrder_ShouldUpdateOnSetLocally()
    {
        // Arrange - Fill cache to max size
        var items = new List<(string key, string value)>();
        for (int i = 0; i < CacheSize; i++)
        {
            items.Add(($"key{i}", $"value{i}"));
            await _replicatedLruCache1.Set(items[i].key, items[i].value);
        }

        // Wait for replication of last item
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(items[CacheSize - 1].key, out _))
            {
                cts1.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts1.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Update first item in cache1
        await _replicatedLruCache1.Set(items[0].key, "new_value");

        // Wait for replication
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(items[0].key, out var v) || v != "new_value")
            {
                cts2.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts2.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Add new item to trigger eviction
        var newKey = "newKey";
        var newValue = "newValue";
        await _replicatedLruCache1.Set(newKey, newValue);

        // Wait for replication
        using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(newKey, out _))
            {
                cts3.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts3.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - In cache1: first item exists (was updated), second item evicted (least recently used)
        Assert.IsTrue(_replicatedLruCache1.TryGet(items[0].key, out var result1));
        Assert.AreEqual("new_value", result1);
        Assert.IsFalse(_replicatedLruCache1.TryGet(items[1].key, out _));
        Assert.IsTrue(_replicatedLruCache1.TryGet(newKey, out _));

        // In cache2: Values are replicated but LRU order is different
        Assert.IsTrue(_replicatedLruCache2.TryGet(items[0].key, out var result2));
        Assert.AreEqual("new_value", result2);
        Assert.IsTrue(_replicatedLruCache2.TryGet(newKey, out _));
    }

    [TestMethod]
    public async Task MaxSize_ShouldBeRespectedIndependentlyInEachInstance()
    {
        // Arrange - Set up test data
        var items = new List<(string key, string value)>();
        for (int i = 0; i < CacheSize + 1; i++)
        {
            items.Add(($"key{i}", $"value{i}"));
            await _replicatedLruCache1.Set(items[i].key, items[i].value);
        }

        // Wait for replication of last item
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(items[CacheSize].key, out _))
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - Count number of items in each cache
        var itemsInCache1 = 0;
        var itemsInCache2 = 0;
        for (int i = 0; i <= CacheSize; i++)
        {
            if (_replicatedLruCache1.TryGet(items[i].key, out _))
            {
                itemsInCache1++;
            }
            if (_replicatedLruCache2.TryGet(items[i].key, out _))
            {
                itemsInCache2++;
            }
        }

        // Verify each cache respects its size limit
        Assert.AreEqual(CacheSize, itemsInCache1);
        Assert.AreEqual(CacheSize, itemsInCache2);

        // Verify last item exists in both caches (as it was the most recently added)
        Assert.IsTrue(_replicatedLruCache1.TryGet(items[CacheSize].key, out _));
        Assert.IsTrue(_replicatedLruCache2.TryGet(items[CacheSize].key, out _));
    }

    [TestMethod]
    public async Task Set_WithTTL_ShouldExpireLocally()
    {
        // Arrange
        var key = "key1";
        var value = "value1";
        var ttl = TimeSpan.FromMilliseconds(500); // Short TTL for testing

        // Act
        await _replicatedLruCache1.Set(key, value, ttl);

        // Assert - Value exists initially
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out var result));
        Assert.AreEqual(value, result);

        // Wait for TTL to expire
        await Task.Delay(ttl + TimeSpan.FromMilliseconds(100));

        // Assert - Value should be expired
        Assert.IsFalse(_replicatedLruCache1.TryGet(key, out _));
    }

    [TestMethod]
    public async Task Set_WithTTL_ShouldExpireAcrossInstances()
    {
        // Arrange
        var key = "key1";
        var value = "value1";
        var ttl = TimeSpan.FromMilliseconds(500); // Short TTL for testing

        // Act
        await _replicatedLruCache1.Set(key, value, ttl);

        // Wait for replication
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v) || v != value)
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - Value exists in both caches initially
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out var result1));
        Assert.AreEqual(value, result1);
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out var result2));
        Assert.AreEqual(value, result2);

        // Wait for TTL to expire
        await Task.Delay(ttl + TimeSpan.FromMilliseconds(100));

        // Assert - Value should be expired in both caches
        Assert.IsFalse(_replicatedLruCache1.TryGet(key, out _));
        Assert.IsFalse(_replicatedLruCache2.TryGet(key, out _));
    }

    [TestMethod]
    public async Task Set_WithTTL_UpdateShouldResetExpiration()
    {
        // Arrange
        var key = "key1";
        var value1 = "value1";
        var value2 = "value2";
        var ttl = TimeSpan.FromMilliseconds(1000);

        // Set initial value with TTL
        await _replicatedLruCache1.Set(key, value1, ttl);

        // Wait for half the TTL
        await Task.Delay(ttl / 2);

        // Update value with same TTL
        await _replicatedLruCache1.Set(key, value2, ttl);

        // Wait for replication
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v) || v != value2)
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Wait for original TTL to expire
        await Task.Delay(ttl / 2 + TimeSpan.FromMilliseconds(100));

        // Assert - Value should still exist with new value
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out var result1));
        Assert.AreEqual(value2, result1);
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out var result2));
        Assert.AreEqual(value2, result2);

        // Wait for new TTL to expire
        await Task.Delay(ttl / 2 + TimeSpan.FromMilliseconds(100));

        // Assert - Value should now be expired
        Assert.IsFalse(_replicatedLruCache1.TryGet(key, out _));
        Assert.IsFalse(_replicatedLruCache2.TryGet(key, out _));
    }

    [TestMethod]
    public async Task Set_WithTTL_ShouldNotAffectLRUEviction()
    {
        // Arrange - Fill cache to max size with TTL items
        var items = new List<(string key, string value)>();
        var ttl = TimeSpan.FromSeconds(30); // Long enough to not interfere with test

        for (int i = 0; i < CacheSize; i++)
        {
            items.Add(($"key{i}", $"value{i}"));
            await _replicatedLruCache1.Set(items[i].key, items[i].value, ttl);
        }

        // Wait for replication of last item
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(items[CacheSize - 1].key, out _))
            {
                cts1.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts1.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for initial replication");
        }

        // Access first item to make it most recently used
        Assert.IsTrue(_replicatedLruCache1.TryGet(items[0].key, out _));

        // Add new item to trigger eviction
        var newKey = "newKey";
        var newValue = "newValue";
        await _replicatedLruCache1.Set(newKey, newValue, ttl);

        // Wait for replication
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(newKey, out _))
            {
                cts2.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts2.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - LRU eviction should work independently of TTL
        Assert.IsTrue(_replicatedLruCache1.TryGet(items[0].key, out _)); // Most recently used item
        Assert.IsFalse(_replicatedLruCache1.TryGet(items[1].key, out _)); // Should be evicted (LRU)
        Assert.IsTrue(_replicatedLruCache1.TryGet(newKey, out _)); // New item

        // Wait a bit to ensure TTL hasn't expired
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert - Items should still exist (TTL not expired)
        Assert.IsTrue(_replicatedLruCache1.TryGet(items[0].key, out _));
        Assert.IsTrue(_replicatedLruCache1.TryGet(newKey, out _));
    }

    [TestMethod]
    public async Task Remove_ShouldReplicateToAllInstances()
    {
        // Arrange - Set initial values
        var key1 = "key1";
        var key2 = "key2";
        var value1 = "value1";
        var value2 = "value2";

        await _replicatedLruCache1.Set(key1, value1);
        await _replicatedLruCache1.Set(key2, value2);

        // Wait for initial replication
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key1, out _) || !_replicatedLruCache2.TryGet(key2, out _) ||
                   !_replicatedLruCache3.TryGet(key1, out _) || !_replicatedLruCache3.TryGet(key2, out _))
            {
                cts1.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts1.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for initial replication");
        }

        // Act - Remove from second instance
        await _replicatedLruCache2.Remove(key1);

        // Wait for remove to replicate
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (_replicatedLruCache1.TryGet(key1, out _) || _replicatedLruCache3.TryGet(key1, out _))
            {
                cts2.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts2.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for remove replication");
        }

        // Assert - key1 should be removed from all instances, key2 should remain
        Assert.IsFalse(_replicatedLruCache1.TryGet(key1, out _));
        Assert.IsFalse(_replicatedLruCache2.TryGet(key1, out _));
        Assert.IsFalse(_replicatedLruCache3.TryGet(key1, out _));

        Assert.IsTrue(_replicatedLruCache1.TryGet(key2, out var result1));
        Assert.AreEqual(value2, result1);
        Assert.IsTrue(_replicatedLruCache2.TryGet(key2, out var result2));
        Assert.AreEqual(value2, result2);
        Assert.IsTrue(_replicatedLruCache3.TryGet(key2, out var result3));
        Assert.AreEqual(value2, result3);
    }

    [TestMethod]
    public async Task Clear_ShouldReplicateToAllInstances()
    {
        // Arrange - Set multiple items
        var items = new List<(string key, string value)>
        {
            ("key1", "value1"),
            ("key2", "value2"),
            ("key3", "value3")
        };

        foreach (var item in items)
        {
            await _replicatedLruCache1.Set(item.key, item.value);
        }

        // Wait for initial replication
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!items.All(item => _replicatedLruCache2.TryGet(item.key, out _)) ||
                   !items.All(item => _replicatedLruCache3.TryGet(item.key, out _)))
            {
                cts1.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts1.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for initial replication");
        }

        // Act - Clear from second instance
        await _replicatedLruCache2.Clear();

        // Wait for clear to replicate
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (items.Any(item => _replicatedLruCache1.TryGet(item.key, out _)) ||
                   items.Any(item => _replicatedLruCache3.TryGet(item.key, out _)))
            {
                cts2.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts2.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for clear replication");
        }

        // Assert - All items should be removed from all instances
        foreach (var item in items)
        {
            Assert.IsFalse(_replicatedLruCache1.TryGet(item.key, out _));
            Assert.IsFalse(_replicatedLruCache2.TryGet(item.key, out _));
            Assert.IsFalse(_replicatedLruCache3.TryGet(item.key, out _));
        }
    }


    [TestMethod]
    public async Task RapidSequentialUpdates_LastWriteWins()
    {
        // Arrange
        var key = "key1";
        var finalValue = "finalValue";

        // Act - Rapidly set multiple values
        await _replicatedLruCache1.Set(key, "value1");
        await Task.Delay(100);
        await _replicatedLruCache2.Set(key, "value2");
        await Task.Delay(100);
        await _replicatedLruCache3.Set(key, "value3");
        await Task.Delay(100);
        await _replicatedLruCache1.Set(key, finalValue); // This should be the winning value

        // Wait for replication to settle
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v2) || v2 != finalValue ||
                   !_replicatedLruCache3.TryGet(key, out var v3) || v3 != finalValue)
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - All instances should have the final value
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out var result1));
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out var result2));
        Assert.IsTrue(_replicatedLruCache3.TryGet(key, out var result3));

        Assert.AreEqual(finalValue, result1);
        Assert.AreEqual(finalValue, result2);
        Assert.AreEqual(finalValue, result3);
    }

    [TestMethod]
    public async Task MessageBus_ShouldDeliverToAllSubscribedInstances()
    {
        // Arrange
        var key = "key1";
        var value = "value1";

        // Act - Set value in first instance
        await _replicatedLruCache1.Set(key, value);

        // Wait for replication
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v2) || v2 != value ||
                   !_replicatedLruCache3.TryGet(key, out var v3) || v3 != value)
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for message delivery");
        }

        // Assert - All instances should receive the message and update their cache
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out var result1));
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out var result2));
        Assert.IsTrue(_replicatedLruCache3.TryGet(key, out var result3));
        Assert.AreEqual(value, result1);
        Assert.AreEqual(value, result2);
        Assert.AreEqual(value, result3);
    }

    [TestMethod]
    public async Task MessageBus_ShouldMaintainMessageOrdering()
    {
        // Arrange
        var key = "key1";
        var values = new[] { "value1", "value2", "value3", "value4", "value5" };

        // Act - Set values in sequence with minimal delay
        foreach (var value in values)
        {
            await _replicatedLruCache1.Set(key, value);
            await Task.Delay(50); // Small delay to ensure message order
        }

        // Wait for final value to replicate
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v2) || v2 != values[^1] ||
                   !_replicatedLruCache3.TryGet(key, out var v3) || v3 != values[^1])
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for message delivery");
        }

        // Assert - All instances should have the final value
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out var result1));
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out var result2));
        Assert.IsTrue(_replicatedLruCache3.TryGet(key, out var result3));
        Assert.AreEqual(values[^1], result1);
        Assert.AreEqual(values[^1], result2);
        Assert.AreEqual(values[^1], result3);
    }

    [TestMethod]
    public async Task MessageBus_ShouldHandleMultipleOperationTypes()
    {
        // Arrange
        var key1 = "key1";
        var key2 = "key2";
        var value1 = "value1";
        var value2 = "value2";

        // Act - Perform different operations
        await _replicatedLruCache1.Set(key1, value1);
        await _replicatedLruCache1.Set(key2, value2);
        await Task.Delay(100);
        await _replicatedLruCache1.Remove(key1);

        // Wait for operations to replicate
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (_replicatedLruCache2.TryGet(key1, out _) || // key1 should be removed
                   !_replicatedLruCache2.TryGet(key2, out var v2) || v2 != value2 || // key2 should exist
                   _replicatedLruCache3.TryGet(key1, out _) ||
                   !_replicatedLruCache3.TryGet(key2, out var v3) || v3 != value2)
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for operations to replicate");
        }

        // Assert - Operations should be applied in order
        Assert.IsFalse(_replicatedLruCache1.TryGet(key1, out _)); // Removed
        Assert.IsFalse(_replicatedLruCache2.TryGet(key1, out _));
        Assert.IsFalse(_replicatedLruCache3.TryGet(key1, out _));

        Assert.IsTrue(_replicatedLruCache1.TryGet(key2, out var result1)); // Still exists
        Assert.IsTrue(_replicatedLruCache2.TryGet(key2, out var result2));
        Assert.IsTrue(_replicatedLruCache3.TryGet(key2, out var result3));
        Assert.AreEqual(value2, result1);
        Assert.AreEqual(value2, result2);
        Assert.AreEqual(value2, result3);
    }

    [TestMethod]
    public async Task MessageBus_ShouldHandleBulkOperations()
    {
        // Arrange
        var items = Enumerable.Range(0, 50)
            .Select(i => (key: $"key{i}", value: $"value{i}"))
            .ToList();

        // Act - Set multiple items rapidly
        var tasks = items.Select(item => _replicatedLruCache1.Set(item.key, item.value));
        await Task.WhenAll(tasks);

        // Wait for replication of last item
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            while (!items.All(item => _replicatedLruCache2.TryGet(item.key, out var v2) && v2 == item.value) ||
                   !items.All(item => _replicatedLruCache3.TryGet(item.key, out var v3) && v3 == item.value))
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for bulk operations to replicate");
        }

        // Assert - All items should be replicated correctly
        foreach (var item in items)
        {
            Assert.IsTrue(_replicatedLruCache1.TryGet(item.key, out var result1));
            Assert.IsTrue(_replicatedLruCache2.TryGet(item.key, out var result2));
            Assert.IsTrue(_replicatedLruCache3.TryGet(item.key, out var result3));
            Assert.AreEqual(item.value, result1);
            Assert.AreEqual(item.value, result2);
            Assert.AreEqual(item.value, result3);
        }
    }

    [TestMethod]
    public async Task ErrorHandling_ShouldAllowNullAndEmptyValues()
    {
        // Arrange
        var key = "testKey";
        string? nullValue = null;
        string emptyValue = "";

        // Act & Assert - Null value
        await _replicatedLruCache1.Set(key, nullValue);
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out var result1));
        Assert.IsNull(result1);

        // Wait for replication
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v2) || v2 != nullValue ||
                   !_replicatedLruCache3.TryGet(key, out var v3) || v3 != nullValue)
            {
                cts1.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts1.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for null value replication");
        }

        // Act & Assert - Empty string value
        await _replicatedLruCache1.Set(key, emptyValue);
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out var result2));
        Assert.AreEqual(emptyValue, result2);

        // Wait for replication
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v2) || v2 != emptyValue ||
                   !_replicatedLruCache3.TryGet(key, out var v3) || v3 != emptyValue)
            {
                cts2.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts2.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for empty value replication");
        }
    }

    [TestMethod]
    public void ErrorHandling_ShouldHandleNonExistentKey()
    {
        // Arrange
        var nonExistentKey = "nonExistentKey";

        // Act & Assert
        Assert.IsFalse(_replicatedLruCache1.TryGet(nonExistentKey, out _));
        Assert.IsFalse(_replicatedLruCache2.TryGet(nonExistentKey, out _));
        Assert.IsFalse(_replicatedLruCache3.TryGet(nonExistentKey, out _));
    }

    [TestMethod]
    public async Task ErrorHandling_ShouldHandleRemoveNonExistentKey()
    {
        // Arrange
        var nonExistentKey = "nonExistentKey";

        // Act - Should not throw
        await _replicatedLruCache1.Remove(nonExistentKey);

        // Assert - Should still not exist
        Assert.IsFalse(_replicatedLruCache1.TryGet(nonExistentKey, out _));
        Assert.IsFalse(_replicatedLruCache2.TryGet(nonExistentKey, out _));
        Assert.IsFalse(_replicatedLruCache3.TryGet(nonExistentKey, out _));
    }

    [TestMethod]
    public async Task ErrorHandling_ShouldHandleDoubleRemove()
    {
        // Arrange
        var key = "key1";
        var value = "value1";
        await _replicatedLruCache1.Set(key, value);

        // Wait for replication
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v2) || v2 != value ||
                   !_replicatedLruCache3.TryGet(key, out var v3) || v3 != value)
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Act - Remove twice
        await _replicatedLruCache1.Remove(key);
        await _replicatedLruCache1.Remove(key); // Should not throw

        // Assert
        Assert.IsFalse(_replicatedLruCache1.TryGet(key, out _));
    }

    [TestMethod]
    public async Task MassiveSimultaneousSets_ShouldReplicateCorrectly()
    {
        // Arrange - Create large sets of data for each instance
        var itemsPerInstance = CacheSize / 3; // Distribute items across instances

        var items1 = Enumerable.Range(0, itemsPerInstance)
            .Select(i => (key: $"instance1_key{i}", value: $"value1_{i}"))
            .ToList();

        var items2 = Enumerable.Range(0, itemsPerInstance)
            .Select(i => (key: $"instance2_key{i}", value: $"value2_{i}"))
            .ToList();

        var items3 = Enumerable.Range(0, itemsPerInstance)
            .Select(i => (key: $"instance3_key{i}", value: $"value3_{i}"))
            .ToList();

        // Act - Set items simultaneously from all instances
        var tasks = new List<Task>();
        tasks.AddRange(items1.Select(item => _replicatedLruCache1.Set(item.key, item.value)));
        tasks.AddRange(items2.Select(item => _replicatedLruCache2.Set(item.key, item.value)));
        tasks.AddRange(items3.Select(item => _replicatedLruCache3.Set(item.key, item.value)));

        await Task.WhenAll(tasks);

        // Wait for replication with increased timeout due to volume
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var allItems = items1.Concat(items2).Concat(items3).ToList();
            while (true)
            {
                var allReplicated = allItems.All(item =>
                    _replicatedLruCache1.TryGet(item.key, out var v1) && v1 == item.value &&
                    _replicatedLruCache2.TryGet(item.key, out var v2) && v2 == item.value &&
                    _replicatedLruCache3.TryGet(item.key, out var v3) && v3 == item.value);

                if (allReplicated)
                    break;

                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for massive operation replication");
        }

        // Assert - Verify all items in all instances
        foreach (var item in items1.Concat(items2).Concat(items3))
        {
            Assert.IsTrue(_replicatedLruCache1.TryGet(item.key, out var value1), $"Key {item.key} not found in cache1");
            Assert.IsTrue(_replicatedLruCache2.TryGet(item.key, out var value2), $"Key {item.key} not found in cache2");
            Assert.IsTrue(_replicatedLruCache3.TryGet(item.key, out var value3), $"Key {item.key} not found in cache3");

            Assert.AreEqual(item.value, value1, $"Value mismatch in cache1 for key {item.key}");
            Assert.AreEqual(item.value, value2, $"Value mismatch in cache2 for key {item.key}");
            Assert.AreEqual(item.value, value3, $"Value mismatch in cache3 for key {item.key}");
        }
    }

    [TestMethod]
    public async Task MessageBus_ShouldHandleDisconnectAndReconnect()
    {
        // Arrange
        var key = "key1";
        var value1 = "value1";
        var value2 = "value2";
        var value3 = "value3";

        Console.WriteLine($"[TEST] Setting initial value '{value1}'");
        // Set initial value
        await _replicatedLruCache1.Set(key, value1);

        // Wait for initial replication
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v2) || v2 != value1)
            {
                cts1.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts1.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for initial replication");
        }
        Console.WriteLine($"[TEST] Initial value replicated to instance 2");

        // Act - Simulate disconnect by stopping service
        Console.WriteLine($"[TEST] Stopping instance 2");
        await _service2.StopAsync(default);
        await Task.Delay(500); // Wait for disconnect to complete

        // Check the state of instance 2 after disconnection but before the update
        _replicatedLruCache2.TryGet(key, out var valueBeforeUpdate);
        Console.WriteLine($"[TEST] Value in instance 2 after disconnection: '{valueBeforeUpdate}'");

        // Set new value while instance 2 is disconnected
        Console.WriteLine($"[TEST] Setting new value '{value2}' while instance 2 is disconnected");
        await _replicatedLruCache1.Set(key, value2);

        // Wait to ensure value2 is replicated to instance 3
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache3.TryGet(key, out var v3) || v3 != value2)
            {
                cts2.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts2.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for value2 replication");
        }
        Console.WriteLine($"[TEST] New value replicated to instance 3");

        // Check the state of instance 2 before reconnection
        _replicatedLruCache2.TryGet(key, out var valueBeforeReconnect);
        Console.WriteLine($"[TEST] Value in instance 2 before reconnection: '{valueBeforeReconnect}'");

        // Get direct reference to the underlying cache implementation to check internal state
        ILruCache? lruCache2 = null;
        try
        {
            lruCache2 = _serviceProvider2.GetKeyedService<ILruCache>(CacheName);
            if (lruCache2 != null)
            {
                bool hasValue = lruCache2.TryGet(key, out var directValue);
                Console.WriteLine($"[TEST] Direct LruCache2 TryGet: success={hasValue}, value='{directValue}'");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TEST] Error getting direct cache reference: {ex.Message}");
        }

        // Reconnect instance 2
        Console.WriteLine($"[TEST] Reconnecting instance 2");
        await _service2.StartAsync(default);
        await Task.Delay(1000); // Wait for reconnection to fully complete

        // Check immediately after reconnection
        _replicatedLruCache2.TryGet(key, out var valueAfterReconnect);
        Console.WriteLine($"[TEST] Value in instance 2 immediately after reconnection: '{valueAfterReconnect}'");

        // Assert - Disconnected instance should still have old value
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out var result2));
        Console.WriteLine($"[TEST] Final read from instance 2: '{result2}'");
        Assert.AreEqual(value1, result2, "Reconnected instance should keep its old value");

        // Other instances should have the new value
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out var result1));
        Assert.IsTrue(_replicatedLruCache3.TryGet(key, out var result3));
        Console.WriteLine($"[TEST] Final read from instance 1: '{result1}'");
        Console.WriteLine($"[TEST] Final read from instance 3: '{result3}'");
        Assert.AreEqual(value2, result1);
        Assert.AreEqual(value2, result3);

        // Verify that new updates after reconnect are received
        Console.WriteLine($"[TEST] Setting final value '{value3}' after reconnection");
        await _replicatedLruCache1.Set(key, value3);

        // Wait for new update to replicate
        using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v2) || v2 != value3 ||
                   !_replicatedLruCache3.TryGet(key, out var v3) || v3 != value3)
            {
                cts3.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts3.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for value3 replication");
        }
        Console.WriteLine($"[TEST] Final value replicated to all instances");

        // Assert - All instances should have the newest value
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out result1));
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out result2));
        Assert.IsTrue(_replicatedLruCache3.TryGet(key, out result3));
        Console.WriteLine($"[TEST] Final verification from instance 1: '{result1}'");
        Console.WriteLine($"[TEST] Final verification from instance 2: '{result2}'");
        Console.WriteLine($"[TEST] Final verification from instance 3: '{result3}'");
        Assert.AreEqual(value3, result1);
        Assert.AreEqual(value3, result2);
        Assert.AreEqual(value3, result3);
    }

    [TestMethod]
    public async Task Instance_ShouldRecoverAfterCrash()
    {
        // Arrange
        var key = "crash_test_key";
        var value1 = "value1";
        var value2 = "value2";

        // Set initial value
        await _replicatedLruCache1.Set(key, value1);

        // Wait for initial replication
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v2) || v2 != value1 ||
                   !_replicatedLruCache3.TryGet(key, out var v3) || v3 != value1)
            {
                cts1.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts1.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for initial replication");
        }

        // Act - Simulate crash by force stopping service
        await _service2.StopAsync(default);
        await Task.Delay(500); // Wait for disconnect to complete

        // Update value while instance is down
        await _replicatedLruCache1.Set(key, value2);

        // Wait for replication to instance 3
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache3.TryGet(key, out var v3) || v3 != value2)
            {
                cts2.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts2.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication to instance 3");
        }

        // Restart crashed instance
        await _service2.StartAsync(default);
        await Task.Delay(500); // Wait for reconnection

        // Assert - Verify state after restart
        // Instance 2 should maintain its local state (value1) since there's no sync mechanism
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out var result2));
        Assert.AreEqual(value1, result2, "Restarted instance should maintain its local state");

        // Other instances should have value2
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out var result1));
        Assert.IsTrue(_replicatedLruCache3.TryGet(key, out var result3));
        Assert.AreEqual(value2, result1);
        Assert.AreEqual(value2, result3);

        // New updates after restart should be replicated to all instances
        var value3 = "value3";
        await _replicatedLruCache1.Set(key, value3);

        // Wait for new update to replicate to ALL instances
        using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v2) || v2 != value3 ||
                   !_replicatedLruCache3.TryGet(key, out var v3) || v3 != value3)
            {
                cts3.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts3.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for new update replication");
        }

        // Assert - All instances should have the newest value
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out result1));
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out result2));
        Assert.IsTrue(_replicatedLruCache3.TryGet(key, out result3));
        Assert.AreEqual(value3, result1);
        Assert.AreEqual(value3, result2);
        Assert.AreEqual(value3, result3);
    }

    [TestMethod]
    public async Task Cache_ShouldHandleStampede()
    {
        // Arrange
        var key = "stampede_key";
        var value = "value1";
        var numRequests = 1000; // High number of concurrent requests

        // Set initial value
        await _replicatedLruCache1.Set(key, value);

        // Wait for initial replication
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_replicatedLruCache2.TryGet(key, out var v2) || v2 != value ||
                   !_replicatedLruCache3.TryGet(key, out var v3) || v3 != value)
            {
                cts1.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts1.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for initial replication");
        }

        // Act - Simulate stampede by making many concurrent get requests
        var tasks = Enumerable.Range(0, numRequests)
            .Select(_ => Task.Run(() =>
            {
                // Randomly choose an instance to simulate distributed load
                var instance = Random.Shared.Next(3) switch
                {
                    0 => _replicatedLruCache1,
                    1 => _replicatedLruCache2,
                    _ => _replicatedLruCache3
                };

                return instance.TryGet(key, out var _);
            }));

        var results = await Task.WhenAll(tasks);

        // Assert - All requests should succeed
        Assert.IsTrue(results.All(r => r));

        // Verify value is still consistent
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out var result1));
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out var result2));
        Assert.IsTrue(_replicatedLruCache3.TryGet(key, out var result3));
        Assert.AreEqual(value, result1);
        Assert.AreEqual(value, result2);
        Assert.AreEqual(value, result3);
    }


    [TestMethod]
    public async Task Cache_ShouldHandleWarmup()
    {
        // Arrange - Create warmup data
        var warmupSize = CacheSize;
        var warmupItems = Enumerable.Range(0, warmupSize)
            .Select(i => (key: $"warmup_key_{i}", value: $"warmup_value_{i}"))
            .ToList();

        // Act - Warm up the cache with parallel sets
        var warmupTasks = warmupItems.Select(item => _replicatedLruCache1.Set(item.key, item.value));
        await Task.WhenAll(warmupTasks);

        // Wait for replication
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (!warmupItems.All(item =>
                _replicatedLruCache2.TryGet(item.key, out var v2) && v2 == item.value &&
                _replicatedLruCache3.TryGet(item.key, out var v3) && v3 == item.value))
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for warmup replication");
        }

        // Verify cache is warmed up
        var successCount1 = 0;
        var successCount2 = 0;
        var successCount3 = 0;

        foreach (var item in warmupItems)
        {
            if (_replicatedLruCache1.TryGet(item.key, out var v1) && v1 == item.value) successCount1++;
            if (_replicatedLruCache2.TryGet(item.key, out var v2) && v2 == item.value) successCount2++;
            if (_replicatedLruCache3.TryGet(item.key, out var v3) && v3 == item.value) successCount3++;
        }

        // Assert - Each cache should have exactly CacheSize items
        Assert.AreEqual(CacheSize, successCount1, "Cache1 has incorrect number of items");
        Assert.AreEqual(CacheSize, successCount2, "Cache2 has incorrect number of items");
        Assert.AreEqual(CacheSize, successCount3, "Cache3 has incorrect number of items");
    }

    [TestMethod]
    public async Task MassiveOverflow_ShouldMaintainMaxSizeAndConsistency()
    {
        // Arrange - Create data exceeding cache size
        var totalItems = CacheSize * 10; // 
        var items = Enumerable.Range(0, totalItems)
            .Select(i => (key: $"overflow_key_{i}", value: $"overflow_value_{i}"))
            .ToList();

        // Act - Rapidly set items exceeding cache size from different instances
        var tasks = new List<Task>();

        // Distribute sets across instances
        for (int i = 0; i < totalItems; i++)
        {
            var item = items[i];
            IReplicatedLruCache instance;
            switch (i % 3)
            {
                case 0:
                    instance = _replicatedLruCache1;
                    break;
                case 1:
                    instance = _replicatedLruCache2;
                    break;
                default:
                    instance = _replicatedLruCache3;
                    break;
            }
            tasks.Add(instance.Set(item.key, item.value));
        }

        await Task.WhenAll(tasks);

        // Wait for replication with increased timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (true)
            {
                // Check if all instances have stabilized at max size
                var size1 = GetCacheSize(_replicatedLruCache1);
                var size2 = GetCacheSize(_replicatedLruCache2);
                var size3 = GetCacheSize(_replicatedLruCache3);

                if (size1 == CacheSize && size2 == CacheSize && size3 == CacheSize)
                    break;

                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for cache size stabilization");
        }

        // Assert - Verify size constraints
        Assert.AreEqual(CacheSize, GetCacheSize(_replicatedLruCache1));
        Assert.AreEqual(CacheSize, GetCacheSize(_replicatedLruCache2));
        Assert.AreEqual(CacheSize, GetCacheSize(_replicatedLruCache3));

        // Verify most recent items are present (last CacheSize items)
        var recentItems = items.Skip(totalItems - CacheSize).ToList();
        var successCount = 0;

        foreach (var item in recentItems)
        {
            // An item should exist in all caches if it exists in any
            var exists1 = _replicatedLruCache1.TryGet(item.key, out var value1);
            var exists2 = _replicatedLruCache2.TryGet(item.key, out var value2);
            var exists3 = _replicatedLruCache3.TryGet(item.key, out var value3);

            if (exists1 || exists2 || exists3)
            {
                successCount++;
                // If it exists, values should match
                if (exists1) Assert.AreEqual(item.value, value1);
                if (exists2) Assert.AreEqual(item.value, value2);
                if (exists3) Assert.AreEqual(item.value, value3);
            }
        }

        // Verify we have a reasonable number of recent items
        // Note: Due to concurrent operations, we might not have exactly CacheSize items
        // but we should have a significant portion of recent items
        Assert.IsTrue(successCount >= CacheSize * 0.8,
            $"Expected at least 80% of recent items to be present, but found only {successCount} items");

        // Helper function to count items in cache
        int GetCacheSize(IReplicatedLruCache cache)
        {
            var count = 0;
            foreach (var item in items)
            {
                if (cache.TryGet(item.key, out _))
                    count++;
            }
            return count;
        }
    }

    [TestMethod]
    public async Task MassiveConcurrentOperations_MaintainsKeyValueIntegrity()
    {
        // Arrange
        var operations = 100000;
        var keyRange = CacheSize - 1;
        var tasks = new List<Task>();
        var random = new Random();

        // Initial population
        for (int i = 0; i < keyRange; i++)
        {
            await _replicatedLruCache1.Set($"key{i}", $"initial_value{i}");
        }

        // Wait for initial replication
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            while (!_replicatedLruCache2.TryGet($"key{keyRange - 1}", out _) ||
                   !_replicatedLruCache3.TryGet($"key{keyRange - 1}", out _))
            {
                cts1.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts1.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for initial replication");
        }

        // Act - Mix of gets and sets with value verification
        for (int i = 0; i < operations; i++)
        {
            var keyIndex = random.Next(keyRange - 10, keyRange * 4); // 4x the number of keys
            var key = $"key{keyIndex}";

            if (i % 2 == 0)
            {
                var newValue = $"value{keyIndex}_{i}";
                IReplicatedLruCache instance;
                switch (i % 3)
                {
                    case 0:
                        instance = _replicatedLruCache1;
                        break;
                    case 1:
                        instance = _replicatedLruCache2;
                        break;
                    default:
                        instance = _replicatedLruCache3;
                        break;
                }
                tasks.Add(instance.Set(key, newValue));
            }
            else
            {
                tasks.Add(Task.Run(() =>
                {
                    // Validate each cache independently
                    void ValidateValue(IReplicatedLruCache cache, string key, int keyIndex)
                    {
                        if (cache.TryGet(key, out var value))
                        {
                            Assert.IsNotNull(value, $"Value should not be null for key: {key}");
                            Assert.IsTrue(
                                value.StartsWith($"initial_value{keyIndex}") ||
                                value.StartsWith($"value{keyIndex}_"),
                                $"Invalid value format in cache: {value} for key: {key}");
                        }
                    }

                    ValidateValue(_replicatedLruCache1, key, keyIndex);
                    ValidateValue(_replicatedLruCache2, key, keyIndex);
                    ValidateValue(_replicatedLruCache3, key, keyIndex);
                }));
            }
        }

        await Task.WhenAll(tasks);

        // Final verification
        var invalidFormatKeys1 = new List<string>();
        var invalidFormatKeys2 = new List<string>();
        var invalidFormatKeys3 = new List<string>();

        // Check each cache independently
        void VerifyCache(IReplicatedLruCache cache, List<string> invalidKeys)
        {
            for (int i = 0; i < keyRange; i++)
            {
                var key = $"key{i}";
                if (cache.TryGet(key, out var value))
                {
                    if (value == null || (!value.StartsWith($"initial_value{i}") && !value.StartsWith($"value{i}_")))
                    {
                        invalidKeys.Add($"{key}: {value}");
                    }
                }
            }
        }

        VerifyCache(_replicatedLruCache1, invalidFormatKeys1);
        VerifyCache(_replicatedLruCache2, invalidFormatKeys2);
        VerifyCache(_replicatedLruCache3, invalidFormatKeys3);

        // Assert each cache independently
        Assert.AreEqual(0, invalidFormatKeys1.Count,
                    $"Found invalid value formats in cache1:\n{string.Join("\n", invalidFormatKeys1)}");
        Assert.AreEqual(0, invalidFormatKeys2.Count,
            $"Found invalid value formats in cache2:\n{string.Join("\n", invalidFormatKeys2)}");
        Assert.AreEqual(0, invalidFormatKeys3.Count,
            $"Found invalid value formats in cache3:\n{string.Join("\n", invalidFormatKeys3)}");
    }
}