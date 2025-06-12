using GobanSource.Bus.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
namespace GobanSource.ReplicatedLruCache;

public static class ReplicatedLruCacheServiceCollectionExtensions
{
    private static IServiceCollection EnsureReplicatedCacheFactory(
        this IServiceCollection services)
    {


        // Only add if not already registered
        if (services.All(x => x.ServiceType != typeof(IReplicatedLruCacheFactory)))
        {
            services.AddSingleton<IReplicatedLruCacheFactory, ReplicatedLruCacheFactory>();
        }
        return services;
    }

    public static IServiceCollection AddReplicatedLruCache<TInterface>(
        this IServiceCollection services,
        int maxSize,
        string? cacheInstanceId = null,
        Action<ReplicatedLruCacheOptions>? configureOptions = null)
        where TInterface : class, IReplicatedLruCache
    {
        if (maxSize <= 0)
            throw new ArgumentException("Cache size must be greater than zero", nameof(maxSize));

        // Use interface name if no ID provided
        var actualCacheId = cacheInstanceId ?? typeof(TInterface).Name;

        services.AddOptions<ReplicatedLruCacheOptions>()
            .BindConfiguration("ReplicatedLruCache")
            .Configure(options => configureOptions?.Invoke(options));

        services.EnsureReplicatedCacheFactory();
        services.EnsureReplicatedCacheInfra();

        services.AddKeyedSingleton<ILruCache>(
            actualCacheId, (sp, key)
                => new LruCache(maxSize, sp.GetRequiredService<ILogger<LruCache>>(), sp.GetService<CacheMetrics>(), actualCacheId));

        services.AddSingleton<TInterface>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ReplicatedLruCacheOptions>>().Value;
            if (string.IsNullOrEmpty(options.AppId))
            {
                throw new ArgumentException("AppId must be configured in ReplicatedLruCacheOptions", nameof(configureOptions));
            }
            var factory = sp.GetRequiredService<IReplicatedLruCacheFactory>();
            return factory.Create<TInterface>(actualCacheId, sp.GetRequiredKeyedService<ILruCache>(actualCacheId),
            sp.GetRequiredService<IRedisSyncBus<CacheMessage>>()
            );
        });

        return services;
    }

    private static IServiceCollection AddRedisSyncBus(
        this IServiceCollection services,
        Action<ReplicatedLruCacheOptions>? configureOptions = null)
    {
        services.AddOptions<ReplicatedLruCacheOptions>()
            .BindConfiguration("ReplicatedLruCache")
            .Configure(options => configureOptions?.Invoke(options));

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ReplicatedLruCacheOptions>>().Value;
            if (string.IsNullOrEmpty(options.RedisSyncBus.ConnectionString))
            {
                throw new ArgumentException("RedisSyncBus.ConnectionString must be configured in ReplicatedLruCacheOptions", nameof(configureOptions));
            }
            return ConnectionMultiplexer.Connect(options.RedisSyncBus.ConnectionString);
        });

        services.AddSingleton<IRedisSyncBus<CacheMessage>>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ReplicatedLruCacheOptions>>().Value;
            if (string.IsNullOrEmpty(options.AppId))
            {
                throw new ArgumentException("AppId must be configured in ReplicatedLruCacheOptions", nameof(configureOptions));
            }
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();

            return new RedisSyncBus<CacheMessage>(
                redis,
                options.AppId,
                options.RedisSyncBus.ChannelPrefix,
                sp.GetRequiredService<ILogger<RedisSyncBus<CacheMessage>>>()
            );
        });

        return services;
    }

    private static IServiceCollection EnsureReplicatedCacheInfra(
        this IServiceCollection services,
        Action<ReplicatedLruCacheOptions>? configureOptions = null)
    {
        if (services.All(x => x.ServiceType == typeof(IRedisSyncBus<CacheMessage>)))
        {
            return services;
        }

        services.AddRedisSyncBus(configureOptions);

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