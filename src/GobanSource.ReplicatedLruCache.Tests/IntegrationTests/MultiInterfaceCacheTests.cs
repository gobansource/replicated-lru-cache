using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using GobanSource.Bus.Redis;

namespace GobanSource.ReplicatedLruCache.Tests.IntegrationTests;

[TestClass]
public class MultiInterfaceCacheTests
{
    private MessageSyncHostedService<CacheMessage> _service1 = null!;
    private MessageSyncHostedService<CacheMessage> _service2 = null!;
    private ITestReplicatedLruCache1 _lruCache1Instance1 = null!;
    private ITestReplicatedLruCache2 _lruCache2Instance1 = null!;
    private ITestReplicatedLruCache1 _lruCache1Instance2 = null!;
    private ITestReplicatedLruCache2 _lruCache2Instance2 = null!;
    private IServiceProvider _serviceProvider1 = null!;
    private IServiceProvider _serviceProvider2 = null!;
    private IRedisSyncBus<CacheMessage> _syncBus1 = null!;
    private IRedisSyncBus<CacheMessage> _syncBus2 = null!;
    private string _appId = null!;
    private const int CacheSize = 100;
    private string _channelPrefix = null!;

    [TestInitialize]
    public void Setup()
    {
        _appId = Guid.NewGuid().ToString();
        _channelPrefix = "test-cache-sync";

        // Create first instance with both caches
        var services1 = new ServiceCollection();
        services1.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services1.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReplicatedLruCache:AppId"] = _appId,
                ["ReplicatedLruCache:RedisSyncBus:ChannelPrefix"] = _channelPrefix,
                ["ReplicatedLruCache:RedisSyncBus:ConnectionString"] = "localhost:6379"
            }).Build());
        services1.AddReplicatedLruCache<ITestReplicatedLruCache1>(CacheSize);
        services1.AddReplicatedLruCache<ITestReplicatedLruCache2>(CacheSize);

        _serviceProvider1 = services1.BuildServiceProvider();
        _service1 = _serviceProvider1.GetServices<IHostedService>().OfType<MessageSyncHostedService<CacheMessage>>().First();
        _syncBus1 = _serviceProvider1.GetRequiredService<IRedisSyncBus<CacheMessage>>();
        _lruCache1Instance1 = _serviceProvider1.GetRequiredService<ITestReplicatedLruCache1>();
        _lruCache2Instance1 = _serviceProvider1.GetRequiredService<ITestReplicatedLruCache2>();

        // Create second instance with both caches
        var services2 = new ServiceCollection();
        services2.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services2.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReplicatedLruCache:AppId"] = _appId,
                ["ReplicatedLruCache:RedisSyncBus:ChannelPrefix"] = _channelPrefix,
                ["ReplicatedLruCache:RedisSyncBus:ConnectionString"] = "localhost:6379"
            }).Build());
        services2.AddReplicatedLruCache<ITestReplicatedLruCache1>(CacheSize);
        services2.AddReplicatedLruCache<ITestReplicatedLruCache2>(CacheSize);

        _serviceProvider2 = services2.BuildServiceProvider();
        _service2 = _serviceProvider2.GetServices<IHostedService>().OfType<MessageSyncHostedService<CacheMessage>>().First();
        _syncBus2 = _serviceProvider2.GetRequiredService<IRedisSyncBus<CacheMessage>>();
        _lruCache1Instance2 = _serviceProvider2.GetRequiredService<ITestReplicatedLruCache1>();
        _lruCache2Instance2 = _serviceProvider2.GetRequiredService<ITestReplicatedLruCache2>();

        // Start all services
        Task.Run(() => _service1.StartAsync(default)).GetAwaiter().GetResult();
        Task.Run(() => _service2.StartAsync(default)).GetAwaiter().GetResult();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_service1 != null)
            await _service1.StopAsync(default);
        if (_service2 != null)
            await _service2.StopAsync(default);

        if (_syncBus1 is IAsyncDisposable disposable1)
            await disposable1.DisposeAsync();
        if (_syncBus2 is IAsyncDisposable disposable2)
            await disposable2.DisposeAsync();

        if (_serviceProvider1 is IAsyncDisposable asyncDisposable1)
            await asyncDisposable1.DisposeAsync();
        if (_serviceProvider2 is IAsyncDisposable asyncDisposable2)
            await asyncDisposable2.DisposeAsync();
    }

    [TestMethod]
    public async Task MultipleInterfaces_ShouldOperateIndependently()
    {
        // Arrange
        var key = "test-key";
        var value1 = "value1";
        var value2 = "value2";

        // Act - Set different values in different caches
        await _lruCache1Instance1.Set(key, value1);
        await _lruCache2Instance1.Set(key, value2);

        // Assert - Each cache should have its own value
        Assert.IsTrue(_lruCache1Instance1.TryGet(key, out var result1));
        Assert.AreEqual(value1, result1);
        Assert.IsTrue(_lruCache2Instance1.TryGet(key, out var result2));
        Assert.AreEqual(value2, result2);

        // Act - Remove from first cache
        await _lruCache1Instance1.Remove(key);

        // Assert - Only first cache should be affected
        Assert.IsFalse(_lruCache1Instance1.TryGet(key, out _));
        Assert.IsTrue(_lruCache2Instance1.TryGet(key, out result2));
        Assert.AreEqual(value2, result2);
    }

    [TestMethod]
    public async Task MultipleInterfaces_WithExplicitIds_ShouldOverrideDefault()
    {
        // Arrange
        var customId = "custom-cache-id";
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReplicatedLruCache:AppId"] = _appId,
                ["ReplicatedLruCache:RedisSyncBus:ChannelPrefix"] = _channelPrefix,
                ["ReplicatedLruCache:RedisSyncBus:ConnectionString"] = "localhost:6379"
            }).Build());

        // Act - Register one cache with custom ID, one with default
        services.AddReplicatedLruCache<ITestReplicatedLruCache1>(CacheSize, customId);
        services.AddReplicatedLruCache<ITestReplicatedLruCache2>(CacheSize);


        var serviceProvider = services.BuildServiceProvider();
        var cache1 = serviceProvider.GetRequiredService<ITestReplicatedLruCache1>();
        var cache2 = serviceProvider.GetRequiredService<ITestReplicatedLruCache2>();

        // Test basic operations
        var key = "test-key";
        var value1 = "value1";
        var value2 = "value2";

        await cache1.Set(key, value1);
        await cache2.Set(key, value2);

        // Assert - Both caches should work independently
        Assert.IsTrue(cache1.TryGet(key, out var result1));
        Assert.AreEqual(value1, result1);
        Assert.IsTrue(cache2.TryGet(key, out var result2));
        Assert.AreEqual(value2, result2);

        if (serviceProvider is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }

    [TestMethod]
    public async Task MultipleInterfaces_Replication_ShouldMaintainIsolation()
    {
        // Arrange
        var key = "test-key";
        var value1 = "value1";
        var value2 = "value2";

        // Act - Set values in first instance
        await _lruCache1Instance1.Set(key, value1);
        await _lruCache2Instance1.Set(key, value2);

        // Wait for replication
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_lruCache1Instance2.TryGet(key, out var v1) || v1 != value1 ||
                   !_lruCache2Instance2.TryGet(key, out var v2) || v2 != value2)
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - Values should be replicated to correct caches
        Assert.IsTrue(_lruCache1Instance2.TryGet(key, out var result1));
        Assert.AreEqual(value1, result1);
        Assert.IsTrue(_lruCache2Instance2.TryGet(key, out var result2));
        Assert.AreEqual(value2, result2);

        // Act - Remove from first cache in first instance
        await _lruCache1Instance1.Remove(key);

        // Wait for replication
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (_lruCache1Instance2.TryGet(key, out _))
            {
                cts2.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts2.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for remove replication");
        }

        // Assert - Only first cache should be affected across instances
        Assert.IsFalse(_lruCache1Instance1.TryGet(key, out _));
        Assert.IsFalse(_lruCache1Instance2.TryGet(key, out _));
        Assert.IsTrue(_lruCache2Instance1.TryGet(key, out result2));
        Assert.AreEqual(value2, result2);
        Assert.IsTrue(_lruCache2Instance2.TryGet(key, out result2));
        Assert.AreEqual(value2, result2);
    }

    [TestMethod]
    public async Task MultipleInterfaces_ConcurrentOperations_ShouldNotInterfere()
    {
        // Arrange
        var numOperations = 100;
        var tasks1 = new List<Task>();
        var tasks2 = new List<Task>();

        // Act - Run concurrent operations on both caches
        for (int i = 0; i < numOperations; i++)
        {
            var key = $"key{i}";
            var value = $"value{i}";
            tasks1.Add(_lruCache1Instance1.Set(key, value));
            tasks2.Add(_lruCache2Instance1.Set(key, $"other_{value}"));
        }

        await Task.WhenAll(tasks1.Concat(tasks2));

        // Wait for replication
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!Enumerable.Range(0, numOperations).All(i =>
                _lruCache1Instance2.TryGet($"key{i}", out var v1) && v1 == $"value{i}" &&
                _lruCache2Instance2.TryGet($"key{i}", out var v2) && v2 == $"other_value{i}"))
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for replication");
        }

        // Assert - Verify all values in both caches
        for (int i = 0; i < numOperations; i++)
        {
            var key = $"key{i}";

            // Check first cache
            Assert.IsTrue(_lruCache1Instance1.TryGet(key, out var result1));
            Assert.AreEqual($"value{i}", result1);
            Assert.IsTrue(_lruCache1Instance2.TryGet(key, out result1));
            Assert.AreEqual($"value{i}", result1);

            // Check second cache
            Assert.IsTrue(_lruCache2Instance1.TryGet(key, out var result2));
            Assert.AreEqual($"other_value{i}", result2);
            Assert.IsTrue(_lruCache2Instance2.TryGet(key, out result2));
            Assert.AreEqual($"other_value{i}", result2);
        }
    }
}