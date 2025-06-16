using GobanSource.Bus.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace GobanSource.ReplicatedLruCache.Tests.UnitTests;

[TestClass]
public class MessageSyncHostedServiceTests
{
    private const string TestCacheName = "test-cache";
    private Mock<IRedisSyncBus<CacheMessage>> _mockSyncBus = null!;
    private Mock<IMessageHandler<CacheMessage>> _mockHandler = null!;
    private Mock<ILogger<MessageSyncHostedService<CacheMessage>>> _mockLogger = null!;
    private MessageSyncHostedService<CacheMessage> _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _mockSyncBus = new Mock<IRedisSyncBus<CacheMessage>>();
        _mockHandler = new Mock<IMessageHandler<CacheMessage>>();
        _mockLogger = new Mock<ILogger<MessageSyncHostedService<CacheMessage>>>();

        _service = new MessageSyncHostedService<CacheMessage>(
            _mockSyncBus.Object,
            _mockHandler.Object,
            _mockLogger.Object);
    }

    [TestMethod]
    public async Task StartAsync_SubscribesToSyncBus()
    {
        // Act
        await _service.StartAsync(default);

        // Assert
        _mockSyncBus.Verify(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()), Times.Once);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Starting") && o.ToString()!.Contains("CacheMessage")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task StopAsync_UnsubscribesFromSyncBus()
    {
        // Act
        await _service.StopAsync(default);

        // Assert
        _mockSyncBus.Verify(x => x.UnsubscribeAsync(), Times.Once);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Stopping") && o.ToString()!.Contains("CacheMessage")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task MessageHandler_WhenMessageIsCorrectType_CallsHandler()
    {
        // Arrange
        Func<CacheMessage, Task>? messageHandler = null;
        _mockSyncBus.Setup(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()))
            .Callback<Func<CacheMessage, Task>, Func<string, CacheMessage>>((handler, _) => messageHandler = handler)
            .Returns(Task.CompletedTask);

        await _service.StartAsync(default);

        var message = new CacheMessage
        {
            CacheName = TestCacheName,
            Operation = CacheOperation.Set,
            Key = "testKey",
            Value = "testValue",
            TTL = TimeSpan.FromMinutes(5)
        };

        _mockHandler.Setup(h => h.HandleAsync(It.IsAny<CacheMessage>()))
            .Returns(Task.CompletedTask);

        // Act
        await messageHandler!(message);

        // Assert
        _mockHandler.Verify(h => h.HandleAsync(message), Times.Once);
    }

    // [TestMethod]
    // public async Task MessageHandler_WhenMessageIsWrongType_DoesNotCallHandler()
    // {
    //     // Arrange
    //     Func<CacheMessage, Task>? messageHandler = null;
    //     _mockSyncBus.Setup(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()))
    //         .Callback<Func<CacheMessage, Task>, Func<string, CacheMessage>>((handler, _) => messageHandler = handler)
    //         .Returns(Task.CompletedTask);

    //     await _service.StartAsync(default);

    //     // Create a different message type
    //     var message = new OtherMessage { SomeProperty = "test" };

    //     // Act
    //     await messageHandler!(messag e);

    //     // Assert
    //     _mockHandler.Verify(h => h.HandleAsync(It.IsAny<CacheMessage>()), Times.Never);
    // }

    [TestMethod]
    public async Task MessageHandler_WhenHandlerThrowsException_LogsErrorAndContinues()
    {
        // Arrange
        Func<CacheMessage, Task>? messageHandler = null;
        _mockSyncBus.Setup(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()))
            .Callback<Func<CacheMessage, Task>, Func<string, CacheMessage>>((handler, _) => messageHandler = handler)
            .Returns(Task.CompletedTask);

        await _service.StartAsync(default);

        var message = new CacheMessage
        {
            CacheName = TestCacheName,
            Operation = CacheOperation.Set,
            Key = "testKey",
            Value = "testValue"
        };

        _mockHandler.Setup(h => h.HandleAsync(It.IsAny<CacheMessage>()))
            .ThrowsAsync(new Exception("Test exception"));

        // Act
        await messageHandler!(message);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Error handling message")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task SubscribeAsync_DeserializerCorrectlyDeserializesJsonToCacheMessage()
    {
        // Arrange
        Func<string, IMessage>? deserializer = null;
        _mockSyncBus.Setup(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()))
            .Callback<Func<CacheMessage, Task>, Func<string, CacheMessage>>((_, deserializeFunc) => deserializer = deserializeFunc)
            .Returns(Task.CompletedTask);

        // Start the service which will set up the deserializer
        await _service.StartAsync(default);

        // Ensure deserializer was captured
        Assert.IsNotNull(deserializer, "Deserializer function should have been provided");

        // Create a JSON string representing a CacheMessage
        var jsonMessage = @"{
            ""MessageId"": ""test-id"",
            ""InstanceId"": ""test-instance"",
            ""Timestamp"": ""2023-01-01T12:00:00Z"",
            ""CacheName"": ""test-cache"",
            ""Operation"": 0,
            ""Key"": ""test-key"",
            ""Value"": ""test-value"",
            ""TTL"": ""00:05:00""
        }";

        // Act
        var result = deserializer!(jsonMessage);

        // Assert
        Assert.IsInstanceOfType(result, typeof(CacheMessage));
        var cacheMessage = (CacheMessage)result;
        Assert.AreEqual("test-id", (result as BaseMessage)?.MessageId);
        Assert.AreEqual("test-instance", (result as BaseMessage)?.InstanceId);
        Assert.AreEqual("test-cache", cacheMessage.CacheName);
        Assert.AreEqual(CacheOperation.Set, cacheMessage.Operation);
        Assert.AreEqual("test-key", cacheMessage.Key);
        Assert.AreEqual("test-value", cacheMessage.Value);
        Assert.AreEqual(TimeSpan.FromMinutes(5), cacheMessage.TTL);
    }

    [TestMethod]
    public async Task SubscribeAsync_DeserializerThrowsExceptionForInvalidJson()
    {
        // Arrange
        Func<string, CacheMessage>? deserializer = null;
        _mockSyncBus.Setup(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()))
            .Callback<Func<CacheMessage, Task>, Func<string, CacheMessage>>((_, deserializeFunc) => deserializer = deserializeFunc)
            .Returns(Task.CompletedTask);

        // Start the service which will set up the deserializer
        await _service.StartAsync(default);

        // Ensure deserializer was captured
        Assert.IsNotNull(deserializer, "Deserializer function should have been provided");

        // Create invalid JSON
        var invalidJson = "{ this is not valid JSON }";

        // Act & Assert
        Assert.ThrowsException<System.Text.Json.JsonException>(() => deserializer!(invalidJson));
    }

    [TestMethod]
    public async Task SubscribeAsync_DeserializerThrowsExceptionForMissingRequiredProperties()
    {
        // Arrange
        Func<string, CacheMessage>? deserializer = null;
        _mockSyncBus.Setup(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()))
            .Callback<Func<CacheMessage, Task>, Func<string, CacheMessage>>((_, deserializeFunc) => deserializer = deserializeFunc)
            .Returns(Task.CompletedTask);

        // Start the service which will set up the deserializer
        await _service.StartAsync(default);

        // Ensure deserializer was captured
        Assert.IsNotNull(deserializer, "Deserializer function should have been provided");

        // Create JSON with missing required properties
        var incompleteJson = @"{
            ""MessageId"": ""test-id"",
            // Missing other required properties
        }";

        // Act & Assert
        Assert.ThrowsException<System.Text.Json.JsonException>(() => deserializer!(incompleteJson));
    }

    [TestMethod]
    public async Task EndToEnd_JsonToHandlerExecution()
    {
        // Arrange
        Func<CacheMessage, Task>? messageHandler = null;
        Func<string, CacheMessage>? deserializer = null;

        _mockSyncBus.Setup(x => x.SubscribeAsync(It.IsAny<Func<CacheMessage, Task>>(), It.IsAny<Func<string, CacheMessage>>()))
            .Callback<Func<CacheMessage, Task>, Func<string, CacheMessage>>((handler, deserializeFunc) =>
            {
                messageHandler = handler;
                deserializer = deserializeFunc;
            })
            .Returns(Task.CompletedTask);

        await _service.StartAsync(default);

        // Ensure both functions were captured
        Assert.IsNotNull(messageHandler, "Message handler should have been provided");
        Assert.IsNotNull(deserializer, "Deserializer function should have been provided");

        // Create a JSON string representing a CacheMessage
        var jsonMessage = @"{
            ""MessageId"": ""test-id"",
            ""InstanceId"": ""test-instance"",
            ""Timestamp"": ""2023-01-01T12:00:00Z"",
            ""CacheName"": ""test-cache"",
            ""Operation"": 0,
            ""Key"": ""test-key"",
            ""Value"": ""test-value"",
            ""TTL"": ""00:05:00""
        }";

        // Setup handler expectation
        _mockHandler.Setup(h => h.HandleAsync(It.IsAny<CacheMessage>()))
            .Returns(Task.CompletedTask);

        // Act - First deserialize, then handle
        var deserializedMessage = deserializer!(jsonMessage);
        await messageHandler!(deserializedMessage);

        // Assert - Verify the handler was called with the deserialized message
        _mockHandler.Verify(h => h.HandleAsync(It.Is<CacheMessage>(m =>
            m.CacheName == "test-cache" &&
            m.Key == "test-key" &&
            m.Value == "test-value" &&
            m.Operation == CacheOperation.Set &&
            m.TTL == TimeSpan.FromMinutes(5))), Times.Once);
    }
}

// A different message type for testing
public class OtherMessage : BaseMessage
{
    public string SomeProperty { get; set; } = null!;

    public OtherMessage()
    {

    }
}