using GobanSource.Bus.Redis;
using GobanSource.ReplicatedLruCache.Tests.Utils;

namespace GobanSource.ReplicatedLruCache.Tests.SociableTests;

[TestClass]
public class CacheSyncHostedServiceTests
{
    private MessageSyncHostedService<CacheMessage> _service1 = null!;
    private MessageSyncHostedService<CacheMessage> _service2 = null!;
    private MessageSyncHostedService<CacheMessage> _service3 = null!;
    private IServiceProvider _serviceProvider1 = null!;
    private IServiceProvider _serviceProvider2 = null!;
    private IServiceProvider _serviceProvider3 = null!;
    private ILruCache _cache1 = null!;
    private ILruCache _cache2 = null!;
    private ILruCache _cache3 = null!;
    private IRedisSyncBus<CacheMessage> _syncBus1 = null!;
    private IRedisSyncBus<CacheMessage> _syncBus2 = null!;
    private IRedisSyncBus<CacheMessage> _syncBus3 = null!;
    private string _appId = null!;
    private const string TestCacheName = "test-cache";

    [TestInitialize]
    public void Setup()
    {
        // Create first instance
        var (service1, serviceProvider1, cache1, syncBus1) =
            CacheSyncHostedServiceFactory.CreateForSociableTest(_appId, TestCacheName);

        // Create second instance
        var (service2, serviceProvider2, cache2, syncBus2) =
            CacheSyncHostedServiceFactory.CreateForSociableTest(_appId, TestCacheName);

        // Create third instance
        var (service3, serviceProvider3, cache3, syncBus3) =
            CacheSyncHostedServiceFactory.CreateForSociableTest(_appId, TestCacheName);

        _service1 = service1;
        _service2 = service2;
        _service3 = service3;
        _serviceProvider1 = serviceProvider1;
        _serviceProvider2 = serviceProvider2;
        _serviceProvider3 = serviceProvider3;
        _cache1 = cache1;
        _cache2 = cache2;
        _cache3 = cache3;
        _syncBus1 = syncBus1;
        _syncBus2 = syncBus2;
        _syncBus3 = syncBus3;
    }

    [TestMethod]
    public async Task StartAsync_SubscribesToSyncBus_AndHandlesMessages()
    {
        // Arrange
        var testMessage = new CacheMessage
        {
            CacheName = TestCacheName,
            Operation = CacheOperation.Set,
            Key = "testKey",
            Value = "testValue",
            TTL = TimeSpan.FromMinutes(5)
        };

        // Start all services
        await _service1.StartAsync(CancellationToken.None);
        await _service2.StartAsync(CancellationToken.None);
        await _service3.StartAsync(CancellationToken.None);

        // Act - Publish from third instance
        await _syncBus3.PublishAsync(testMessage);

        // Wait for message processing with timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!_cache1.TryGet("testKey", out _) || !_cache2.TryGet("testKey", out _))
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timeout waiting for message to be processed");
        }

        // Assert - First and second instances should receive and process the message
        Assert.IsTrue(_cache1.TryGet("testKey", out var value1));
        Assert.AreEqual("testValue", value1);
        Assert.IsTrue(_cache2.TryGet("testKey", out var value2));
        Assert.AreEqual("testValue", value2);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await CacheSyncHostedServiceFactory.CleanupAsync(_service1, _serviceProvider1, _syncBus1);
        await CacheSyncHostedServiceFactory.CleanupAsync(_service2, _serviceProvider2, _syncBus2);
        await CacheSyncHostedServiceFactory.CleanupAsync(_service3, _serviceProvider3, _syncBus3);
    }
}