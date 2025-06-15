using System.Text.Json;
using GobanSource.Bus.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace GobanSource.ReplicatedLruCache.Tests.UnitTests;

[TestClass]
public class RedisSyncBusTests
{
    private Mock<IConnectionMultiplexer> _mockRedis = null!;
    private Mock<ISubscriber> _mockSubscriber = null!;
    private ILogger<RedisSyncBus<CacheMessage>> _logger = null!;
    private RedisSyncBus<CacheMessage> _bus = null!;
    private string _appId = null!;
    private const string ChannelPrefix = "test-prefix";

    [TestInitialize]
    public void Setup()
    {
        _appId = Guid.NewGuid().ToString();
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockSubscriber = new Mock<ISubscriber>();
        _mockRedis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(_mockSubscriber.Object);
        _logger = NullLogger<RedisSyncBus<CacheMessage>>.Instance;
        _bus = new RedisSyncBus<CacheMessage>(_mockRedis.Object, _appId, ChannelPrefix, _logger);
    }

    [TestMethod]
    public async Task PublishAsync_ShouldPublishToCorrectChannel()
    {
        // Arrange
        var message = new CacheMessage
        {
            AppId = _appId,
            CacheName = "test-cache",
            Operation = CacheOperation.Set,
            Key = "test-key",
            Value = "test-value",
            TTL = TimeSpan.FromMinutes(5)
        };

        // Act
        await _bus.PublishAsync(message);

        // Assert
        _mockSubscriber.Verify(s => s.PublishAsync(
            It.Is<RedisChannel>(c => c.ToString() == $"{ChannelPrefix}:{_appId}:{typeof(CacheMessage).Name}"),
            It.Is<RedisValue>(v => IsValidMessageJson(v.ToString(), message, _bus.GetInstanceId())),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    // [TestMethod]
    // public async Task PublishAsync_WhenAppIdMismatch_ShouldThrowException()
    // {
    //     // Arrange
    //     var message = new CacheMessage
    //     {
    //         CacheName = "test-cache",
    //         Operation = CacheOperation.Set,
    //         Key = "test-key",
    //         Value = "test-value"
    //     };

    //     // Act & Assert
    //     await Assert.ThrowsExceptionAsync<InvalidOperationException>(
    //         () => _bus.PublishAsync(message));

    //     _mockSubscriber.Verify(s => s.PublishAsync(
    //         It.IsAny<RedisChannel>(),
    //         It.IsAny<RedisValue>(),
    //         It.IsAny<CommandFlags>()),
    //         Times.Never);
    // }

    [TestMethod]
    public async Task SubscribeAsync_ShouldSubscribeToCorrectChannelPattern()
    {
        // Arrange
        var handler = new Func<IMessage, Task>(msg => Task.CompletedTask);

        // Act
        await _bus.SubscribeAsync(handler, json => JsonSerializer.Deserialize<CacheMessage>(json));

        // Assert
        _mockSubscriber.Verify(s => s.SubscribeAsync(
            It.Is<RedisChannel>(c => c.ToString() == $"{ChannelPrefix}:{_appId}:{typeof(CacheMessage).Name}"),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [TestMethod]
    public async Task SubscribeAsync_WhenAlreadySubscribed_ShouldThrowException()
    {
        // Arrange
        var handler = new Func<IMessage, Task>(msg => Task.CompletedTask);
        await _bus.SubscribeAsync(handler, json => JsonSerializer.Deserialize<CacheMessage>(json));

        // Act & Assert
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _bus.SubscribeAsync(handler, json => JsonSerializer.Deserialize<CacheMessage>(json)));
    }

    [TestMethod]
    public async Task UnsubscribeAsync_ShouldUnsubscribeFromChannel()
    {
        // Arrange
        var handler = new Func<IMessage, Task>(msg => Task.CompletedTask);
        await _bus.SubscribeAsync(handler, json => JsonSerializer.Deserialize<CacheMessage>(json));

        // Act
        await _bus.UnsubscribeAsync();

        // Assert
        _mockSubscriber.Verify(s => s.UnsubscribeAsync(
            It.Is<RedisChannel>(c => c.ToString() == $"{ChannelPrefix}:{_appId}:{typeof(CacheMessage).Name}"),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [TestMethod]
    public async Task UnsubscribeAsync_WhenNotSubscribed_ShouldDoNothing()
    {
        // Act
        await _bus.UnsubscribeAsync();

        // Assert
        _mockSubscriber.Verify(s => s.UnsubscribeAsync(
            It.IsAny<RedisChannel>(),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [TestMethod]
    public async Task MessageHandler_ShouldSkipMessagesFromSameInstance()
    {
        // Arrange
        var handlerCalled = false;
        var handler = new Func<IMessage, Task>(_ =>
        {
            handlerCalled = true;
            return Task.CompletedTask;
        });

        Action<RedisChannel, RedisValue> subscriberCallback = null!;
        _mockSubscriber.Setup(s => s.SubscribeAsync(
            It.IsAny<RedisChannel>(),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((_, callback, _) => subscriberCallback = callback)
            .Returns(Task.CompletedTask);

        await _bus.SubscribeAsync(handler, json => JsonSerializer.Deserialize<CacheMessage>(json));

        var message = new CacheMessage
        {
            AppId = _appId,
            CacheName = "test-cache",
            Operation = CacheOperation.Set,
            Key = "test-key",
            Value = "test-value",
            InstanceId = _bus.GetInstanceId() // Same instance ID
        };

        // Act
        subscriberCallback(new RedisChannel("test", RedisChannel.PatternMode.Auto), JsonSerializer.Serialize(message));

        // Assert
        Assert.IsFalse(handlerCalled, "Handler should not be called for messages from same instance");
    }

    [TestMethod]
    public async Task MessageHandler_ShouldSkipMessagesFromDifferentAppId()
    {
        // Arrange
        var handlerCalled = false;
        var handler = new Func<IMessage, Task>(_ =>
        {
            handlerCalled = true;
            return Task.CompletedTask;
        });

        Action<RedisChannel, RedisValue> subscriberCallback = null!;
        _mockSubscriber.Setup(s => s.SubscribeAsync(
            It.IsAny<RedisChannel>(),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((_, callback, _) => subscriberCallback = callback)
            .Returns(Task.CompletedTask);

        await _bus.SubscribeAsync(handler, json => JsonSerializer.Deserialize<CacheMessage>(json));

        var message = new CacheMessage
        {
            AppId = "different-app-id",
            CacheName = "test-cache",
            Operation = CacheOperation.Set,
            Key = "test-key",
            Value = "test-value",
            InstanceId = Guid.NewGuid().ToString()
        };

        // Act
        subscriberCallback(new RedisChannel("test", RedisChannel.PatternMode.Auto), JsonSerializer.Serialize(message));

        // Assert
        Assert.IsFalse(handlerCalled, "Handler should not be called for messages from different AppId");
    }

    [TestMethod]
    public async Task MessageHandler_ShouldProcessValidMessages()
    {
        // Arrange
        var handlerCallCount = 0;
        string? receivedMessageId = null;
        string? receivedAppId = null;
        string? receivedInstanceId = null;

        var handler = new Func<IMessage, Task>(msg =>
        {
            handlerCallCount++;
            receivedMessageId = msg.MessageId;
            receivedAppId = msg.AppId;
            receivedInstanceId = msg.InstanceId;
            Console.WriteLine($"Handler called with message: {JsonSerializer.Serialize(msg)}");
            return Task.CompletedTask;
        });

        Action<RedisChannel, RedisValue> subscriberCallback = null!;
        _mockSubscriber.Setup(s => s.SubscribeAsync(
            It.IsAny<RedisChannel>(),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((channel, callback, _) =>
            {
                Console.WriteLine($"Subscription: Channel={channel}");
                subscriberCallback = callback;
            })
            .Returns(Task.CompletedTask);

        await _bus.SubscribeAsync(handler, json => JsonSerializer.Deserialize<CacheMessage>(json));

        var messageInstanceId = Guid.NewGuid().ToString(); // Different instance ID
        var message = new CacheMessage
        {
            AppId = _appId,
            CacheName = "test-cache",
            Operation = CacheOperation.Set,
            Key = "test-key",
            Value = "test-value",
            InstanceId = messageInstanceId
        };

        // Capture what we expect should happen
        Console.WriteLine($"Message AppId = {message.AppId}");
        Console.WriteLine($"Bus AppId = {_appId}");
        Console.WriteLine($"Bus InstanceId = {_bus.GetInstanceId()}");
        Console.WriteLine($"Message InstanceId = {messageInstanceId}");

        // Act
        var serializedMessage = JsonSerializer.Serialize(message);
        Console.WriteLine($"Serialized message: {serializedMessage}");

        var channelUsed = $"{ChannelPrefix}:{_appId}";
        Console.WriteLine($"Channel used for message delivery: {channelUsed}");

        subscriberCallback(new RedisChannel(channelUsed, RedisChannel.PatternMode.Pattern), serializedMessage);

        // Assert
        Console.WriteLine($"Handler called {handlerCallCount} times");
        if (handlerCallCount > 0)
        {
            Console.WriteLine($"Received message - MessageId: {receivedMessageId}, AppId: {receivedAppId}, InstanceId: {receivedInstanceId}");
        }

        Assert.IsTrue(handlerCallCount > 0, "Handler should be called for valid messages");
        Assert.AreEqual(_appId, receivedAppId, "Message AppId should match");
        Assert.AreEqual(messageInstanceId, receivedInstanceId, "Message InstanceId should match the one we set");
    }

    private bool IsValidMessageJson(string json, IMessage originalMessage, string expectedInstanceId)
    {
        try
        {
            Console.WriteLine($"[DEBUG] Validating message JSON: {json}");
            Console.WriteLine($"[DEBUG] Expected AppId: {originalMessage.AppId}, Expected InstanceId: {expectedInstanceId}");

            var message = JsonSerializer.Deserialize<CacheMessage>(json);
            Console.WriteLine($"[DEBUG] Deserialized message: AppId={message?.AppId}, InstanceId={message?.InstanceId}");

            var result = message != null &&
                   message.AppId == originalMessage.AppId &&
                   message.InstanceId == expectedInstanceId;

            Console.WriteLine($"[DEBUG] JSON validation result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error validating message JSON: {ex.Message}");
            return false;
        }
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_bus != null)
        {
            await _bus.DisposeAsync();
        }
    }
}