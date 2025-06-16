using System.Text.Json;
using GobanSource.Bus.Redis;
using StackExchange.Redis;

namespace GobanSource.ReplicatedLruCache.Tests.IntegrationTests;
[TestClass]
public class RedisSyncBusIntegrationTests : IAsyncDisposable
{
    private IConnectionMultiplexer _redis = null!;
    private string _cacheName = null!;
    private string _channelPrefix = null!;
    private IRedisSyncBus<CacheMessage> _provider = null!;

    [TestInitialize]
    public void Initialize()
    {
        _cacheName = $"test-cache-{Guid.NewGuid()}";
        _channelPrefix = $"test-cache-sync-{Guid.NewGuid()}";

        _provider = CreateSyncBus(_channelPrefix);
    }

    private IRedisSyncBus<CacheMessage> CreateSyncBus(string channelPrefix)
    {
        return RedisSyncBusFactory.CreateForIntegrationTest(channelPrefix);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task MessagesFromSameInstance_AreSkipped()
    {
        // Arrange
        var processedMessages = new List<IMessage>();
        await _provider.SubscribeAsync(message =>
        {
            processedMessages.Add(message);
            return Task.CompletedTask;
        }, json => JsonSerializer.Deserialize<CacheMessage>(json));

        // Act
        var testMessage = new CacheMessage
        {
            CacheName = _cacheName,
            Key = "test-key",
            Value = "test-value",
            Operation = CacheOperation.Set
        };

        await _provider.PublishAsync(testMessage);

        // Wait briefly to ensure message processing
        await Task.Delay(100);

        // Assert
        Assert.AreEqual(0, processedMessages.Count, "Message from same instance should be skipped");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task MessagesFromDifferentInstances_AreProcessed()
    {
        // Arrange
        var processedMessages1 = new List<IMessage>();
        var processedMessages2 = new List<IMessage>();
        var processedMessages3 = new List<IMessage>();
        var provider1 = _provider; // Use existing provider

        // Create second and third providers with same app/cache IDs but different instances
        var provider2 = CreateSyncBus(_channelPrefix);

        var provider3 = CreateSyncBus(_channelPrefix);

        try
        {
            await provider1.SubscribeAsync(message =>
            {
                processedMessages1.Add(message);
                return Task.CompletedTask;
            }, json => JsonSerializer.Deserialize<CacheMessage>(json));

            await provider2.SubscribeAsync(message =>
            {
                processedMessages2.Add(message);
                return Task.CompletedTask;
            }, json => JsonSerializer.Deserialize<CacheMessage>(json));

            await provider3.SubscribeAsync(message =>
            {
                processedMessages3.Add(message);
                return Task.CompletedTask;
            }, json => JsonSerializer.Deserialize<CacheMessage>(json));

            // Act - Send message from third provider
            var testMessage = new CacheMessage
            {
                CacheName = _cacheName,
                Key = "test-key",
                Value = "test-value",
                Operation = CacheOperation.Set
            };

            await provider3.PublishAsync(testMessage);

            // Wait for message processing with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                while (processedMessages1.Count == 0 || processedMessages2.Count == 0)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    await Task.Delay(100, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Assert.Fail($"Timeout waiting for messages to be processed. Provider1: {processedMessages1.Count}, Provider2: {processedMessages2.Count}");
            }

            // Assert
            Assert.AreEqual(1, processedMessages1.Count, "Provider1 should receive the message");
            Assert.AreEqual(1, processedMessages2.Count, "Provider2 should receive the message");
            Assert.AreEqual(0, processedMessages3.Count, "Provider3 (sender) should not receive its own message");

            // Verify message content for both receivers
            var received1 = (CacheMessage)processedMessages1[0];
            var received2 = (CacheMessage)processedMessages2[0];

            // Both should receive same message
            Assert.AreEqual(testMessage.Key, received1.Key);
            Assert.AreEqual(testMessage.Value, received1.Value);
            Assert.AreEqual(testMessage.Key, received2.Key);
            Assert.AreEqual(testMessage.Value, received2.Value);

            // Both should have sender's instance ID
            Assert.IsNotNull(received1.InstanceId, "Message should have instance ID");
            Assert.IsNotNull(received2.InstanceId, "Message should have instance ID");
            Assert.AreEqual(received1.InstanceId, received2.InstanceId, "Both receivers should see same sender ID");
        }
        finally
        {
            await provider2.DisposeAsync();
            await provider3.DisposeAsync();
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task MessageBroadcast_ReceivedByAllOtherInstances()
    {
        // Arrange
        var processedMessages1 = new List<IMessage>();
        var processedMessages2 = new List<IMessage>();
        var processedMessages3 = new List<IMessage>();
        var provider1 = _provider; // Use existing provider

        // Create two more providers with same app/cache IDs but different instances
        var provider2 = RedisSyncBusFactory.CreateForIntegrationTest(_channelPrefix);

        var provider3 = RedisSyncBusFactory.CreateForIntegrationTest(_channelPrefix);

        try
        {
            // Subscribe all providers
            await provider1.SubscribeAsync(message =>
            {
                processedMessages1.Add(message);
                return Task.CompletedTask;
            }, json => JsonSerializer.Deserialize<CacheMessage>(json));

            await provider2.SubscribeAsync(message =>
            {
                processedMessages2.Add(message);
                return Task.CompletedTask;
            }, json => JsonSerializer.Deserialize<CacheMessage>(json));

            await provider3.SubscribeAsync(message =>
            {
                processedMessages3.Add(message);
                return Task.CompletedTask;
            }, json => JsonSerializer.Deserialize<CacheMessage>(json));

            // Act - Send message from third provider
            var testMessage = new CacheMessage
            {
                CacheName = _cacheName,
                Key = "test-key",
                Value = "test-value",
                Operation = CacheOperation.Set
            };

            await provider3.PublishAsync(testMessage);

            // Wait for message processing with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                while (processedMessages1.Count == 0 || processedMessages2.Count == 0)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    await Task.Delay(100, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Assert.Fail($"Timeout waiting for messages to be processed. Provider1: {processedMessages1.Count}, Provider2: {processedMessages2.Count}, Provider3: {processedMessages3.Count}");
            }

            // Assert
            Assert.AreEqual(1, processedMessages1.Count, "Provider1 should receive the message");
            Assert.AreEqual(1, processedMessages2.Count, "Provider2 should receive the message");
            Assert.AreEqual(0, processedMessages3.Count, "Provider3 (sender) should not receive its own message");

            // Verify message content for both receivers
            var received1 = (CacheMessage)processedMessages1[0];
            var received2 = (CacheMessage)processedMessages2[0];

            // Both should receive same message
            Assert.AreEqual(testMessage.Key, received1.Key);
            Assert.AreEqual(testMessage.Value, received1.Value);
            Assert.AreEqual(testMessage.Key, received2.Key);
            Assert.AreEqual(testMessage.Value, received2.Value);

            // Both should have sender's instance ID
            Assert.IsNotNull(received1.InstanceId, "Message should have instance ID");
            Assert.IsNotNull(received2.InstanceId, "Message should have instance ID");
            Assert.AreEqual(received1.InstanceId, received2.InstanceId, "Both receivers should see same sender ID");

            // Now test message from second provider
            var testMessage2 = new CacheMessage
            {
                CacheName = _cacheName,
                Key = "test-key-2",
                Value = "test-value-2",
                Operation = CacheOperation.Set
            };

            await provider2.PublishAsync(testMessage2);

            // Wait for message processing with timeout
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                while (processedMessages1.Count == 1 || processedMessages3.Count == 0)
                {
                    cts2.Token.ThrowIfCancellationRequested();
                    await Task.Delay(100, cts2.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Assert.Fail($"Timeout waiting for second message. Provider1: {processedMessages1.Count}, Provider2: {processedMessages2.Count}, Provider3: {processedMessages3.Count}");
            }

            // Assert second message
            Assert.AreEqual(2, processedMessages1.Count, "Provider1 should receive both messages");
            Assert.AreEqual(1, processedMessages2.Count, "Provider2 should not receive its own message");
            Assert.AreEqual(1, processedMessages3.Count, "Provider3 should receive message from Provider2");

            // Verify second message content
            var received1Second = (CacheMessage)processedMessages1[1];
            var received3 = (CacheMessage)processedMessages3[0];

            Assert.AreEqual(testMessage2.Key, received1Second.Key);
            Assert.AreEqual(testMessage2.Value, received1Second.Value);
            Assert.AreEqual(testMessage2.Key, received3.Key);
            Assert.AreEqual(testMessage2.Value, received3.Value);
        }
        finally
        {
            await provider2.DisposeAsync();
            await provider3.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider != null)
        {
            await _provider.DisposeAsync();
        }
        if (_redis != null)
        {
            await _redis.DisposeAsync();
        }
    }
}