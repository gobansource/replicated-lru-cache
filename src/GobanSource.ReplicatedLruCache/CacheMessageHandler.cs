using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using GobanSource.Bus.Redis;

namespace GobanSource.ReplicatedLruCache;

/// <summary>
/// Handles cache synchronization messages by updating the appropriate cache.
/// </summary>
public class CacheMessageHandler : IMessageHandler<CacheMessage>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CacheMessageHandler> _logger;

    public CacheMessageHandler(IServiceProvider serviceProvider, ILogger<CacheMessageHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(CacheMessage message)
    {
        Console.WriteLine($"[DEBUG] CacheMessageHandler processing message: Op={message.Operation}, Key={message.Key}, CacheName={message.CacheName}");

        var cache = _serviceProvider.GetKeyedService<ILruCache>(message.CacheName);

        if (cache == null)
        {
            Console.WriteLine($"[DEBUG] Cache not found for instance: {message.CacheName}");
            _logger.LogWarning("Cache not found for instance: {CacheName}", message.CacheName);
            return;
        }

        try
        {
            switch (message.Operation)
            {
                case CacheOperation.Set:
                    Console.WriteLine($"[DEBUG] Setting cache: Key={message.Key}, Value={message.Value}");
                    cache.Set(message.Key, message.Value, message.TTL);
                    _logger.LogDebug("Set {Key} in cache {CacheName}", message.Key, message.CacheName);
                    break;
                case CacheOperation.Remove:
                    Console.WriteLine($"[DEBUG] Removing from cache: Key={message.Key}");
                    cache.Remove(message.Key);
                    _logger.LogDebug("Removed {Key} from cache {CacheName}", message.Key, message.CacheName);
                    break;
                case CacheOperation.Clear:
                    Console.WriteLine($"[DEBUG] Clearing cache");
                    cache.Clear();
                    _logger.LogDebug("Cleared cache {CacheName}", message.CacheName);
                    break;
                default:
                    Console.WriteLine($"[DEBUG] Unknown cache operation: {message.Operation}");
                    _logger.LogWarning("Unknown cache operation: {Operation}", message.Operation);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error processing cache message: {ex.Message}");
            _logger.LogError(ex, "Error processing cache sync message: {Message}", message);
        }
    }
}