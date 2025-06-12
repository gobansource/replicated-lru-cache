using GobanSource.Bus.Redis;
using GobanSource.ReplicatedLruCache.Tests.Utils;
using Microsoft.Extensions.Logging;
using Moq;

namespace GobanSource.ReplicatedLruCache.Tests.UnitTests;

[TestClass]
public class CacheSyncHostedServiceTests
{
    private MessageSyncHostedService<CacheMessage> _service = null!;
    private Mock<ILruCache> _mockCache = null!;
    private Mock<IRedisSyncBus<CacheMessage>> _mockSyncBus = null!;
    private Mock<ILogger<MessageSyncHostedService<CacheMessage>>> _mockLogger = null!;
    private Mock<ILogger<CacheMessageHandler>> _mockHandlerLogger = null!;
    private string _appId = null!;
    private const string TestCacheInstanceId = "test-cache";

    [TestInitialize]
    public void Setup()
    {
        _appId = Guid.NewGuid().ToString();
        (_service, _, _mockCache, _mockSyncBus, _mockLogger, _mockHandlerLogger) =
            CacheSyncHostedServiceFactory.CreateForUnitTest(_appId, TestCacheInstanceId);
    }

    [TestMethod]
    public async Task StartAsync_SubscribesToSyncBus()
    {
        // Act
        await _service.StartAsync(default);

        // Assert
        _mockSyncBus.Verify(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()), Times.Once);
    }

    [TestMethod]
    public async Task StopAsync_UnsubscribesFromSyncBus()
    {
        // Act
        await _service.StopAsync(default);

        // Assert
        _mockSyncBus.Verify(x => x.UnsubscribeAsync(), Times.Once);
    }

    [TestMethod]
    public async Task MessageHandler_WhenSetOperation_UpdatesCache()
    {
        // Arrange
        Func<CacheMessage, Task> messageHandler = null!;
        _mockSyncBus.Setup(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()))
            .Callback<Func<CacheMessage, Task>, Func<string, CacheMessage>>((handler, _) => messageHandler = handler)
            .Returns(Task.CompletedTask);

        await _service.StartAsync(default);

        var message = new CacheMessage
        {
            CacheInstanceId = TestCacheInstanceId,
            Operation = CacheOperation.Set,
            Key = "testKey",
            Value = "testValue",
            TTL = TimeSpan.FromMinutes(5)
        };

        // Act
        await messageHandler(message);

        // Assert
        _mockCache.Verify(c => c.Set(message.Key, message.Value, message.TTL), Times.Once);
        _mockHandlerLogger.Verify(l => l.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o != null && o.ToString()!.Contains("Set") && o.ToString()!.Contains(message.Key)),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task MessageHandler_WhenRemoveOperation_RemovesFromCache()
    {
        // Arrange
        Func<CacheMessage, Task> messageHandler = null!;
        _mockSyncBus.Setup(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()))
            .Callback<Func<CacheMessage, Task>, Func<string, CacheMessage>>((handler, _) => messageHandler = handler)
            .Returns(Task.CompletedTask);

        await _service.StartAsync(default);

        var message = new CacheMessage
        {
            CacheInstanceId = TestCacheInstanceId,
            Operation = CacheOperation.Remove,
            Key = "testKey"
        };

        // Act
        await messageHandler(message);

        // Assert
        _mockCache.Verify(c => c.Remove(message.Key), Times.Once);
        _mockHandlerLogger.Verify(l => l.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o != null && o.ToString()!.Contains("Removed") && o.ToString()!.Contains(message.Key)),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task MessageHandler_WhenClearOperation_ClearsCache()
    {
        // Arrange
        Func<CacheMessage, Task> messageHandler = null!;
        _mockSyncBus.Setup(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()))
            .Callback<Func<CacheMessage, Task>, Func<string, CacheMessage>>((handler, _) => messageHandler = handler)
            .Returns(Task.CompletedTask);

        await _service.StartAsync(default);

        var message = new CacheMessage
        {
            CacheInstanceId = TestCacheInstanceId,
            Operation = CacheOperation.Clear
        };

        // Act
        await messageHandler(message);

        // Assert
        _mockCache.Verify(c => c.Clear(), Times.Once);
        _mockHandlerLogger.Verify(l => l.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o != null && o.ToString()!.Contains("Cleared")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task MessageHandler_WhenCacheNotFound_LogsWarning()
    {
        // Arrange
        Func<CacheMessage, Task> messageHandler = null!;
        _mockSyncBus.Setup(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()))
            .Callback<Func<CacheMessage, Task>, Func<string, CacheMessage>>((handler, _) => messageHandler = handler)
            .Returns(Task.CompletedTask);

        // _mockServiceProvider.Setup(sp => sp.GetService(typeof(ILruCache)))
        //     .Returns(null);

        await _service.StartAsync(default);

        var message = new CacheMessage
        {
            CacheInstanceId = "non-existent-cache",
            Operation = CacheOperation.Set,
            Key = "testKey",
            Value = "testValue"
        };

        // Act
        await messageHandler(message);

        // Assert
        _mockHandlerLogger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o != null && o.ToString()!.Contains("Cache not found")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task MessageHandler_WhenCacheInstanceIdDoesNotMatch_LogsWarning()
    {
        // Arrange
        Func<CacheMessage, Task> messageHandler = null!;
        _mockSyncBus.Setup(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()))
            .Callback<Func<CacheMessage, Task>, Func<string, CacheMessage>>((handler, _) => messageHandler = handler)
            .Returns(Task.CompletedTask);

        await _service.StartAsync(default);

        var message = new CacheMessage
        {
            CacheInstanceId = "different-cache-instance",
            Operation = CacheOperation.Set,
            Key = "testKey",
            Value = "testValue"
        };

        // Act
        await messageHandler(message);

        // Assert
        _mockHandlerLogger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o != null && o.ToString()!.Contains("Cache not found") && o.ToString()!.Contains("different-cache-instance")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify cache was not modified
        _mockCache.Verify(c => c.Set(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [TestMethod]
    public async Task MessageHandler_WhenOperationFails_LogsErrorAndContinues()
    {
        // Arrange
        Func<CacheMessage, Task> messageHandler = null!;
        _mockSyncBus.Setup(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()))
            .Callback<Func<CacheMessage, Task>, Func<string, CacheMessage>>((handler, _) => messageHandler = handler)
            .Returns(Task.CompletedTask);

        _mockCache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .Throws(new InvalidOperationException("Test error"));

        await _service.StartAsync(default);

        var message = new CacheMessage
        {
            CacheInstanceId = TestCacheInstanceId,
            Operation = CacheOperation.Set,
            Key = "testKey",
            Value = "testValue"
        };

        // Act
        await messageHandler(message);

        // Assert
        _mockHandlerLogger.Verify(l => l.Log<It.IsAnyType>(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => true),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _service.StopAsync(default);
    }
}
