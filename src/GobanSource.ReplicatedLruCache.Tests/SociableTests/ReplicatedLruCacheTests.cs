using GobanSource.Bus.Redis;
using GobanSource.ReplicatedLruCache.Tests.Utils;

namespace GobanSource.ReplicatedLruCache.Tests.SociableTests;

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
    private const string CacheInstanceId = "test-cache";
    private const int CacheSize = 100;

    [TestInitialize]
    public void Setup()
    {
        _appId = Guid.NewGuid().ToString();
        Console.WriteLine($"[TEST] Setup with appId={_appId}");

        // Create first instance
        var (service1, serviceProvider1, cache1, syncBus1) =
            CacheSyncHostedServiceFactory.CreateForSociableTest(_appId, CacheInstanceId, CacheSize);

        // Create second instance
        var (service2, serviceProvider2, cache2, syncBus2) =
            CacheSyncHostedServiceFactory.CreateForSociableTest(_appId, CacheInstanceId, CacheSize);

        // Create third instance
        var (service3, serviceProvider3, cache3, syncBus3) =
            CacheSyncHostedServiceFactory.CreateForSociableTest(_appId, CacheInstanceId, CacheSize);

        _service1 = service1;
        _service2 = service2;
        _service3 = service3;
        _serviceProvider1 = serviceProvider1;
        _serviceProvider2 = serviceProvider2;
        _serviceProvider3 = serviceProvider3;
        _syncBus1 = syncBus1;
        _syncBus2 = syncBus2;
        _syncBus3 = syncBus3;

        // Create replicated caches
        Console.WriteLine("[TEST] Creating replicated caches");
        ReplicatedLruCacheFactory factory = new ReplicatedLruCacheFactory();
        _replicatedLruCache1 = factory.Create<IReplicatedLruCache>(CacheInstanceId, cache1, syncBus1);
        _replicatedLruCache2 = factory.Create<IReplicatedLruCache>(CacheInstanceId, cache2, syncBus2);
        _replicatedLruCache3 = factory.Create<IReplicatedLruCache>(CacheInstanceId, cache3, syncBus3);

        // Start all services
        Console.WriteLine("[TEST] Starting services");
        Task.Run(() => _service1.StartAsync(default)).GetAwaiter().GetResult();
        Task.Run(() => _service2.StartAsync(default)).GetAwaiter().GetResult();
        Task.Run(() => _service3.StartAsync(default)).GetAwaiter().GetResult();
        Console.WriteLine("[TEST] Services started");
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await CacheSyncHostedServiceFactory.CleanupAsync(_service1, _serviceProvider1, _syncBus1);
        await CacheSyncHostedServiceFactory.CleanupAsync(_service2, _serviceProvider2, _syncBus2);
        await CacheSyncHostedServiceFactory.CleanupAsync(_service3, _serviceProvider3, _syncBus3);
    }

    [TestMethod]
    public async Task Set_ShouldReplicateToOtherInstances()
    {
        // Arrange
        var key = "key1";
        var value = "value1";
        var ttl = TimeSpan.FromMinutes(5);
        Console.WriteLine($"[TEST] Starting Set_ShouldReplicateToOtherInstances with key={key}, value={value}");

        // Act - Set in first instance
        Console.WriteLine("[TEST] Setting value in first cache");
        await _replicatedLruCache1.Set(key, value, ttl);

        // Wait for replication with timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Console.WriteLine("[TEST] Waiting for replication...");
        try
        {
            int attempts = 0;
            while (!_replicatedLruCache2.TryGet(key, out _) || !_replicatedLruCache3.TryGet(key, out _))
            {
                attempts++;
                Console.WriteLine($"[TEST] Replication wait attempt {attempts}");
                Console.WriteLine($"[TEST] Cache2 has value: {_replicatedLruCache2.TryGet(key, out var v2)}");
                Console.WriteLine($"[TEST] Cache3 has value: {_replicatedLruCache3.TryGet(key, out var v3)}");

                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
            Console.WriteLine($"[TEST] Replication completed after {attempts} attempts");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[TEST] Replication TIMEOUT!");
            Console.WriteLine($"[TEST] Final check - Cache2 has value: {_replicatedLruCache2.TryGet(key, out var v2)}");
            Console.WriteLine($"[TEST] Final check - Cache3 has value: {_replicatedLruCache3.TryGet(key, out var v3)}");
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - Other instances should have the value
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out var replicatedValue2));
        Assert.AreEqual(value, replicatedValue2);
        Assert.IsTrue(_replicatedLruCache3.TryGet(key, out var replicatedValue3));
        Assert.AreEqual(value, replicatedValue3);
        Console.WriteLine("[TEST] Set_ShouldReplicateToOtherInstances completed successfully");
    }

    [TestMethod]
    public async Task Remove_ShouldReplicateToOtherInstances()
    {
        // Arrange
        var key = "key1";
        var value = "value1";
        await _replicatedLruCache1.Set(key, value);
        await Task.Delay(100);
        await _replicatedLruCache2.Set(key, value);
        await Task.Delay(100);
        await _replicatedLruCache3.Set(key, value);
        await Task.Delay(100);

        // Verify all caches have the value
        Assert.IsTrue(_replicatedLruCache1.TryGet(key, out _));
        Assert.IsTrue(_replicatedLruCache2.TryGet(key, out _));
        Assert.IsTrue(_replicatedLruCache3.TryGet(key, out _));

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
}