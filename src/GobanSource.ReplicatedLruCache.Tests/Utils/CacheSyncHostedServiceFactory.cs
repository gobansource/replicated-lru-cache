using GobanSource.Bus.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace GobanSource.ReplicatedLruCache.Tests.Utils;

public static class CacheSyncHostedServiceFactory
{
    public static (MessageSyncHostedService<CacheMessage> service, IServiceProvider serviceProvider, Mock<ILruCache> mockCache, Mock<IRedisSyncBus<CacheMessage>> mockSyncBus, Mock<ILogger<MessageSyncHostedService<CacheMessage>>> mockLogger, Mock<ILogger<CacheMessageHandler>> mockHandlerLogger)
        CreateForUnitTest(
            string appId,
            string cacheInstanceId)
    {
        var services = new ServiceCollection();

        // Set up mocks
        var mockCache = new Mock<ILruCache>();
        var mockSyncBus = new Mock<IRedisSyncBus<CacheMessage>>();
        var mockLogger = new Mock<ILogger<MessageSyncHostedService<CacheMessage>>>();
        var mockCacheMessageHandlerLogger = new Mock<ILogger<CacheMessageHandler>>();

        // Register services
        services.AddKeyedSingleton<ILruCache>(cacheInstanceId, (sp, key) => mockCache.Object);
        services.AddSingleton<IRedisSyncBus<CacheMessage>>(mockSyncBus.Object);
        services.AddSingleton(mockLogger.Object);
        services.AddSingleton<IMessageHandler<CacheMessage>>(sp => new CacheMessageHandler(sp, mockCacheMessageHandlerLogger.Object));

        var serviceProvider = services.BuildServiceProvider();

        // Create service
        var service = new MessageSyncHostedService<CacheMessage>(
            mockSyncBus.Object,
            serviceProvider.GetRequiredService<IMessageHandler<CacheMessage>>(),
            mockLogger.Object);

        return (service, serviceProvider, mockCache, mockSyncBus, mockLogger, mockCacheMessageHandlerLogger);
    }

    public static (MessageSyncHostedService<CacheMessage> service, IServiceProvider serviceProvider, ILruCache cache, IRedisSyncBus<CacheMessage> syncBus)
        CreateForSociableTest(
            string appId,
            string cacheInstanceId,
            int cacheSize = 1000)
    {
        var services = new ServiceCollection();

        // Set up cache
        var cache = new LruCache(cacheSize);
        services.AddKeyedSingleton<ILruCache>(cacheInstanceId, cache);

        // Set up sync bus
        var syncBus = RedisSyncBusFactory.CreateForTest(appId);
        services.AddSingleton<IRedisSyncBus<CacheMessage>>(syncBus);

        // Set up loggers
        var loggerMock = new Mock<ILogger<MessageSyncHostedService<CacheMessage>>>();
        services.AddSingleton(loggerMock.Object);

        var handlerLoggerMock = new Mock<ILogger<CacheMessageHandler>>();
        services.AddSingleton(handlerLoggerMock.Object);

        // Register the message handler
        services.AddSingleton<IMessageHandler<CacheMessage>>(sp => new CacheMessageHandler(sp, handlerLoggerMock.Object));

        var serviceProvider = services.BuildServiceProvider();

        // Create service with the resolved message handler
        var messageHandler = serviceProvider.GetRequiredService<IMessageHandler<CacheMessage>>();
        var service = new MessageSyncHostedService<CacheMessage>(
            syncBus,
            messageHandler,
            loggerMock.Object);

        return (service, serviceProvider, cache, syncBus);
    }

    public static async Task CleanupAsync(
        MessageSyncHostedService<CacheMessage> service,
        IServiceProvider serviceProvider,
        IRedisSyncBus<CacheMessage> syncBus)
    {
        if (service != null)
        {
            await service.StopAsync(default);
        }

        if (syncBus is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }

        if (serviceProvider is IDisposable disposableProvider)
        {
            disposableProvider.Dispose();
        }
    }
}