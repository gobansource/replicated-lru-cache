﻿using GobanSource.Bus.Redis;
using GobanSource.ReplicatedLruCache;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Please provide an instance ID as a command line argument.");
            return;
        }

        var cacheName = args[0];
        Console.WriteLine($"Starting cache instance: {cacheName}");

        var host = CreateHostBuilder(cacheName).Build();
        await host.RunAsync();
    }

    static IHostBuilder CreateHostBuilder(string cacheName) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                // Create and register the shared Redis connection
                var redis = ConnectionMultiplexer.Connect("localhost:6379");
                services.AddSingleton<IConnectionMultiplexer>(redis);

                services.AddReplicatedLruCache<IReplicatedLruCache>(
                    maxSize: 1000,
                    cacheName: cacheName,
                    connectionMultiplexer: redis,
                    configureOptions: options =>
                    {
                        options.RedisSyncBus.ChannelPrefix = "cache-sync";
                    });

                // Add our interactive console service
                services.AddHostedService<InteractiveConsoleService>();
                services.AddSingleton<string>(cacheName); // Pass instance ID to the service
            });
}

public class InteractiveConsoleService : BackgroundService
{
    private readonly IReplicatedLruCache _cache;
    private readonly string _cacheName;
    private readonly IHostApplicationLifetime _hostLifetime;

    public InteractiveConsoleService(
        IReplicatedLruCache cache,
        string cacheName,
        IHostApplicationLifetime hostLifetime)
    {
        _cache = cache;
        _cacheName = cacheName;
        _hostLifetime = hostLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit for other services to start
        await Task.Delay(1000, stoppingToken);

        Console.WriteLine($"Instance {_cacheName} is ready and listening for cache updates...");
        Console.WriteLine("Press Ctrl+C to exit\n");

        while (!stoppingToken.IsCancellationRequested)
        {
            Console.WriteLine("\nCache Operations:");
            Console.WriteLine("1. Set value");
            Console.WriteLine("2. Get value");
            Console.WriteLine("3. Remove value");
            Console.WriteLine("4. Clear cache");
            Console.WriteLine("5. Exit");
            Console.Write("\nSelect operation (1-5): ");

            var choice = Console.ReadLine();

            if (stoppingToken.IsCancellationRequested)
                break;

            switch (choice)
            {
                case "1":
                    await HandleSet(stoppingToken);
                    break;
                case "2":
                    HandleGet();
                    break;
                case "3":
                    await HandleRemove(stoppingToken);
                    break;
                case "4":
                    await HandleClear(stoppingToken);
                    break;
                case "5":
                    _hostLifetime.StopApplication();
                    return;
                default:
                    Console.WriteLine("Invalid choice. Please try again.");
                    break;
            }
        }
    }

    private async Task HandleSet(CancellationToken cancellationToken)
    {
        Console.Write("Enter key: ");
        var key = Console.ReadLine();
        Console.Write("Enter value: ");
        var value = Console.ReadLine();
        Console.Write("Enter TTL in seconds (optional, press Enter to skip): ");
        var ttlInput = Console.ReadLine();

        TimeSpan? ttl = null;
        if (!string.IsNullOrEmpty(ttlInput) && int.TryParse(ttlInput, out var seconds))
        {
            ttl = TimeSpan.FromSeconds(seconds);
        }

        await _cache.Set(key!, value!, ttl);
        Console.WriteLine($"[{_cacheName}] Value set successfully. Key: {key}, Value: {value}, TTL: {ttl?.TotalSeconds ?? 0}s");
        Console.WriteLine($"[{_cacheName}] This should now be replicated to other instances!");
    }

    private void HandleGet()
    {
        Console.Write("Enter key: ");
        var key = Console.ReadLine();

        if (_cache.TryGet(key!, out var value))
        {
            Console.WriteLine($"[{_cacheName}] Value: {value}");
        }
        else
        {
            Console.WriteLine($"[{_cacheName}] Key not found.");
        }
    }

    private async Task HandleRemove(CancellationToken cancellationToken)
    {
        Console.Write("Enter key to remove: ");
        var key = Console.ReadLine();

        await _cache.Remove(key!);
        Console.WriteLine($"[{_cacheName}] Key {key} removed successfully.");
        Console.WriteLine($"[{_cacheName}] This removal should be replicated to other instances!");
    }

    private async Task HandleClear(CancellationToken cancellationToken)
    {
        await _cache.Clear();
        Console.WriteLine($"[{_cacheName}] Cache cleared successfully.");
        Console.WriteLine($"[{_cacheName}] This clear should be replicated to other instances!");
    }
}
