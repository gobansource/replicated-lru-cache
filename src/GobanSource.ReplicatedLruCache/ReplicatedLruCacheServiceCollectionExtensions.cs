using GobanSource.Bus.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Linq;
namespace GobanSource.ReplicatedLruCache;

public static class ReplicatedLruCacheServiceCollectionExtensions
{
    public static IServiceCollection AddReplicatedLruCache<TInterface>(
        this IServiceCollection services,
        int maxSize,
        string? cacheName = null,
        Action<ReplicatedLruCacheOptions>? configureOptions = null,
        IConnectionMultiplexer? connectionMultiplexer = null)
        where TInterface : class, IReplicatedLruCache
    {
        if (maxSize <= 0)
            throw new ArgumentException("Cache size must be greater than zero", nameof(maxSize));

        // Use interface name if no ID provided
        var actualCacheName = cacheName ?? typeof(TInterface).Name;

        services.AddOptions<ReplicatedLruCacheOptions>()
            .BindConfiguration("ReplicatedLruCache")
            .Configure(options => configureOptions?.Invoke(options));

        services.EnsureReplicatedCacheInfra(connectionMultiplexer, configureOptions);

        services.AddKeyedSingleton<ILruCache>(
            actualCacheName, (sp, key)
                => new LruCache(maxSize, sp.GetRequiredService<ILogger<LruCache>>(), sp.GetService<CacheMetrics>(), actualCacheName));

        services.AddSingleton<TInterface>(sp =>
        {
            return ReplicatedLruCacheProxy<TInterface>.Create(
                new ReplicatedLruCache(
                    sp.GetRequiredKeyedService<ILruCache>(actualCacheName),
                    sp.GetRequiredService<IRedisSyncBus<CacheMessage>>(),
                    actualCacheName)
            );
        });

        return services;
    }

    private static IServiceCollection AddRedisSyncBus(
        this IServiceCollection services,
        IConnectionMultiplexer? externalMultiplexer = null,
        Action<ReplicatedLruCacheOptions>? configureOptions = null)
    {
        services.AddOptions<ReplicatedLruCacheOptions>()
            .BindConfiguration("ReplicatedLruCache")
            .Configure(options => configureOptions?.Invoke(options));

        // Ensure we have a multiplexer: either supplied directly or already registered in DI
        var hasMultiplexerInDi = services.Any(x => x.ServiceType == typeof(IConnectionMultiplexer));
        if (!hasMultiplexerInDi && externalMultiplexer == null)
        {
            throw new InvalidOperationException("IConnectionMultiplexer must be registered in the service collection or supplied to AddReplicatedLruCache");
        }

        services.AddSingleton<IRedisSyncBus<CacheMessage>>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ReplicatedLruCacheOptions>>().Value;
            var redis = externalMultiplexer ?? sp.GetRequiredService<IConnectionMultiplexer>();

            return new RedisSyncBus<CacheMessage>(
                redis,
                options.RedisSyncBus.ChannelPrefix,
                sp.GetRequiredService<ILogger<RedisSyncBus<CacheMessage>>>(),
                enableCompression: true
            );
        });

        return services;
    }

    private static IServiceCollection EnsureReplicatedCacheInfra(
        this IServiceCollection services,
        IConnectionMultiplexer? connectionMultiplexer = null,
        Action<ReplicatedLruCacheOptions>? configureOptions = null)
    {
        if (services.Any(x => x.ServiceType == typeof(IRedisSyncBus<CacheMessage>)))
        {
            return services;
        }

        services.AddRedisSyncBus(connectionMultiplexer, configureOptions);

        // Register the message handler for cache messages
        services.AddSingleton<IMessageHandler<CacheMessage>, CacheMessageHandler>();

        // Register the generic hosted service for cache messages
        services.AddHostedService<MessageSyncHostedService<CacheMessage>>();

        return services;
    }

    public static IServiceCollection AddReplicatedCacheMetrics(
        this IServiceCollection services)
    {
        if (services.All(x => x.ServiceType == typeof(CacheMetrics)))
        {
            return services;
        }
        services.AddSingleton<CacheMetrics>();
        return services;
    }
}