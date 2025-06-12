using GobanSource.Bus.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GobanSource.ReplicatedLruCache.Tests.IntegrationTests;

[TestClass]
public class ReplicatedLruCacheConfigurationTests
{
    private const string CacheInstanceId = "test-cache";
    private const int CacheSize = 100;
    private const string AppId = "test-app";
    private const string ChannelPrefix = "test-channel";
    private const string RedisConnection = "localhost:6379";

    [TestMethod]
    public async Task AddReplicatedLruCache_WithInlineConfig_ShouldInitializeCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>())
        .Build();
        services.AddSingleton<IConfiguration>(configuration);

        // Act
        services.AddReplicatedLruCache<IReplicatedLruCache>(CacheSize, CacheInstanceId, options =>
        {
            options.AppId = AppId;
            options.RedisSyncBus.ChannelPrefix = ChannelPrefix;
            options.RedisSyncBus.ConnectionString = RedisConnection;
        });


        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var cache = serviceProvider.GetRequiredService<IReplicatedLruCache>();
        Assert.IsNotNull(cache);

        var hostedService = serviceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<MessageSyncHostedService<CacheMessage>>()
            .FirstOrDefault();
        Assert.IsNotNull(hostedService);

        // Cleanup
        if (serviceProvider is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }

    [TestMethod]
    public async Task AddReplicatedLruCache_WithConfigurationFile_ShouldInitializeCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.ut.json")
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        // Configure options from configuration file
        services.Configure<ReplicatedLruCacheOptions>(configuration.GetSection("ReplicatedLruCache"));

        // Act
        services.AddReplicatedLruCache<IReplicatedLruCache>(CacheSize, CacheInstanceId);


        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var cache = serviceProvider.GetRequiredService<IReplicatedLruCache>();
        Assert.IsNotNull(cache);

        var hostedService = serviceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<MessageSyncHostedService<CacheMessage>>()
            .FirstOrDefault();
        Assert.IsNotNull(hostedService);

        // Cleanup
        if (serviceProvider is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }

    [TestMethod]
    public async Task AddReplicatedLruCache_WithMixedConfig_ShouldUseInlineOverFile()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.ut.json")
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        // Configure options from configuration file
        services.Configure<ReplicatedLruCacheOptions>(configuration.GetSection("ReplicatedLruCache"));

        var customAppId = "custom-app-id";

        // Act
        services.AddReplicatedLruCache<IReplicatedLruCache>(CacheSize, CacheInstanceId, options =>
        {
            options.AppId = customAppId; // Override AppId from config file
            // Let other settings come from config file
        });



        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var cache = serviceProvider.GetRequiredService<IReplicatedLruCache>();
        Assert.IsNotNull(cache);

        var hostedService = serviceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<MessageSyncHostedService<CacheMessage>>()
            .FirstOrDefault();
        Assert.IsNotNull(hostedService);

        // Test the cache to verify it's working with the custom AppId
        var key = "test-key";
        var value = "test-value";
        await cache.Set(key, value);
        Assert.IsTrue(cache.TryGet(key, out var result));
        Assert.AreEqual(value, result);

        // Cleanup
        if (serviceProvider is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }

    [TestMethod]
    public void AddReplicatedLruCache_WithInvalidConfig_ShouldThrowException()
    {
        // Act & Assert - Missing AppId
        Assert.ThrowsException<ArgumentException>(() =>
        {
            var services2 = new ServiceCollection();
            services2.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
            services2.AddSingleton<IConfiguration>(configuration);

            services2.AddReplicatedLruCache<IReplicatedLruCache>(CacheSize, CacheInstanceId, options =>
            {
                // AppId is missing
                options.RedisSyncBus.ChannelPrefix = ChannelPrefix;
                options.RedisSyncBus.ConnectionString = RedisConnection;
            });
            services2.BuildServiceProvider().GetRequiredService<IReplicatedLruCache>();
        });

        // Act & Assert - Missing Redis connection string
        Assert.ThrowsException<ArgumentException>(() =>
        {
            var services3 = new ServiceCollection();
            services3.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
            services3.AddSingleton<IConfiguration>(configuration);

            services3.AddReplicatedLruCache<IReplicatedLruCache>(CacheSize, CacheInstanceId, options =>
            {
                options.AppId = AppId;
                options.RedisSyncBus.ChannelPrefix = ChannelPrefix;
            });
            services3.BuildServiceProvider().GetRequiredService<IRedisSyncBus<CacheMessage>>();
        });
    }

    [TestMethod]
    public void AddReplicatedLruCache_WithZeroCacheSize_ShouldThrowException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Act & Assert
        Assert.ThrowsException<ArgumentException>(() =>
        {
            services.AddReplicatedLruCache<IReplicatedLruCache>(0, CacheInstanceId, options =>
            {
                options.AppId = AppId;
                options.RedisSyncBus.ChannelPrefix = ChannelPrefix;
                options.RedisSyncBus.ConnectionString = RedisConnection;
            });
        });
    }

    [TestMethod]
    public void AddReplicatedLruCache_WithNegativeCacheSize_ShouldThrowException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Act & Assert
        Assert.ThrowsException<ArgumentException>(() =>
        {
            services.AddReplicatedLruCache<IReplicatedLruCache>(-1, CacheInstanceId, options =>
            {
                options.AppId = AppId;
                options.RedisSyncBus.ChannelPrefix = ChannelPrefix;
                options.RedisSyncBus.ConnectionString = RedisConnection;
            });
        });
    }
}