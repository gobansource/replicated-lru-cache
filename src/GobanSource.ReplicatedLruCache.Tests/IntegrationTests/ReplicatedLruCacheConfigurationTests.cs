using GobanSource.Bus.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace GobanSource.ReplicatedLruCache.Tests.IntegrationTests;

[TestClass]
public class ReplicatedLruCacheConfigurationTests
{
    private const string CacheName = "test-cache";
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
        var mux = ConnectionMultiplexer.Connect("localhost:6379");
        services.AddSingleton<IConnectionMultiplexer>(mux);

        // Act
        services.AddReplicatedLruCache<IReplicatedLruCache>(CacheSize, CacheName, options =>
        {
            options.RedisSyncBus.ChannelPrefix = ChannelPrefix;
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
        var mux = ConnectionMultiplexer.Connect("localhost:6379");
        services.AddSingleton<IConnectionMultiplexer>(mux);

        // Configure options from configuration file
        services.Configure<ReplicatedLruCacheOptions>(configuration.GetSection("ReplicatedLruCache"));

        // Act
        services.AddReplicatedLruCache<IReplicatedLruCache>(CacheSize, CacheName);


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
    public void AddReplicatedLruCache_WithZeroCacheSize_ShouldThrowException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Act & Assert
        Assert.ThrowsException<ArgumentException>(() =>
        {
            services.AddReplicatedLruCache<IReplicatedLruCache>(0, CacheName, options =>
            {
                options.RedisSyncBus.ChannelPrefix = ChannelPrefix;
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
            services.AddReplicatedLruCache<IReplicatedLruCache>(-1, CacheName, options =>
            {
                options.RedisSyncBus.ChannelPrefix = ChannelPrefix;
            });
        });
    }
}