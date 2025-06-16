using GobanSource.Bus.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace GobanSource.ReplicatedLruCache.Tests;

public static class RedisSyncBusFactory
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<Action<RedisChannel, RedisValue>>> _channelHandlers = new();
    private static readonly Mock<IConnectionMultiplexer> _redisMock = new();
    private static readonly Mock<ISubscriber> _subscriberMock = new();

    static RedisSyncBusFactory()
    {
        _redisMock.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(_subscriberMock.Object);

        _subscriberMock.Setup(s => s.SubscribeAsync(
            It.IsAny<RedisChannel>(),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((channel, handler, _) =>
            {
                var channelName = channel.ToString();
                var handlers = _channelHandlers.GetOrAdd(channelName, _ => new ConcurrentBag<Action<RedisChannel, RedisValue>>());
                handlers.Add(handler);
            })
            .Returns(Task.CompletedTask);

        _subscriberMock.Setup(s => s.PublishAsync(
            It.IsAny<RedisChannel>(),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((channel, value, _) =>
            {
                var publishedChannel = channel.ToString();
                foreach (var pattern in _channelHandlers.Keys)
                {
                    var regexPattern = "^" + pattern.Replace("*", ".*") + "$";
                    if (System.Text.RegularExpressions.Regex.IsMatch(publishedChannel, regexPattern))
                    {
                        if (_channelHandlers.TryGetValue(pattern, out var handlers))
                        {
                            foreach (var handler in handlers)
                            {
                                handler(channel, value);
                            }
                        }
                    }
                }
            })
            .Returns(Task.FromResult(1L));
    }

    public static IRedisSyncBus<CacheMessage> CreateForTest(string channelPrefix = "cache-sync")
    {
        var loggerMock = new Mock<ILogger<RedisSyncBus<CacheMessage>>>();
        return new RedisSyncBus<CacheMessage>(
            _redisMock.Object,
            channelPrefix,
            loggerMock.Object);
    }

    //Create For Integration Tests
    public static IRedisSyncBus<CacheMessage> CreateForIntegrationTest(string channelPrefix)
    {
        // Initialize Redis connection
        var redis = ConnectionMultiplexer.Connect("localhost:6379");

        return new RedisSyncBus<CacheMessage>(
            redis,
            channelPrefix,
            NullLogger<RedisSyncBus<CacheMessage>>.Instance,
            enableCompression: true
        );
    }
}
