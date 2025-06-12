
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace GobanSource.ReplicatedLruCache.Tests.UnitTests;

[TestClass]
public class CacheMessageHandlerTests
{
    private const string TestCacheInstanceId = "test-cache";
    private IServiceProvider _serviceProvider = null!;
    private Mock<ILruCache> _mockCache = null!;
    private Mock<ILogger<CacheMessageHandler>> _mockLogger = null!;
    private CacheMessageHandler _handler = null!;

    [TestInitialize]
    public void Initialize()
    {
        // Create mocks
        _mockCache = new Mock<ILruCache>();
        _mockLogger = new Mock<ILogger<CacheMessageHandler>>();

        // Create a real service collection and register our mock
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ILruCache>(TestCacheInstanceId, (sp, key) => _mockCache.Object);

        // Build a real service provider
        _serviceProvider = services.BuildServiceProvider();

        // Create the handler with the real ServiceProvider
        _handler = new CacheMessageHandler(_serviceProvider, _mockLogger.Object);
    }

    [TestMethod]
    public async Task HandleAsync_WhenSetOperation_SetsCache()
    {
        // Arrange
        var message = new CacheMessage
        {
            CacheInstanceId = TestCacheInstanceId,
            Operation = CacheOperation.Set,
            Key = "testKey",
            Value = "testValue",
            TTL = TimeSpan.FromMinutes(5)
        };

        // Act
        await _handler.HandleAsync(message);

        // Assert
        _mockCache.Verify(c => c.Set(message.Key, message.Value, message.TTL), Times.Once);
        _mockLogger.Verify(l => l.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o != null && o.ToString()!.Contains("Set") && o.ToString()!.Contains(message.Key)),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_WhenRemoveOperation_RemovesFromCache()
    {
        // Arrange
        var message = new CacheMessage
        {
            CacheInstanceId = TestCacheInstanceId,
            Operation = CacheOperation.Remove,
            Key = "testKey"
        };

        // Act
        await _handler.HandleAsync(message);

        // Assert
        _mockCache.Verify(c => c.Remove(message.Key), Times.Once);
        _mockLogger.Verify(l => l.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o != null && o.ToString()!.Contains("Removed") && o.ToString()!.Contains(message.Key)),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_WhenClearOperation_ClearsCache()
    {
        // Arrange
        var message = new CacheMessage
        {
            CacheInstanceId = TestCacheInstanceId,
            Operation = CacheOperation.Clear
        };

        // Act
        await _handler.HandleAsync(message);

        // Assert
        _mockCache.Verify(c => c.Clear(), Times.Once);
        _mockLogger.Verify(l => l.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o != null && o.ToString()!.Contains("Cleared")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_WhenCacheNotFound_LogsWarning()
    {
        // Arrange
        var message = new CacheMessage
        {
            CacheInstanceId = "non-existent-cache",
            Operation = CacheOperation.Set,
            Key = "testKey",
            Value = "testValue"
        };

        // Act
        await _handler.HandleAsync(message);

        // Assert
        _mockLogger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o != null && o.ToString()!.Contains("Cache not found")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_WhenOperationFails_LogsErrorAndContinues()
    {
        // Arrange
        _mockCache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .Throws(new Exception("Test exception"));

        var message = new CacheMessage
        {
            CacheInstanceId = TestCacheInstanceId,
            Operation = CacheOperation.Set,
            Key = "testKey",
            Value = "testValue"
        };

        // Act
        await _handler.HandleAsync(message);

        // Assert
        _mockLogger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o != null && o.ToString()!.Contains("Error processing cache sync message")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}