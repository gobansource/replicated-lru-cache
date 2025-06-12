
namespace GobanSource.ReplicatedLruCache;

public class RedisSyncBusOptions
{
    public string ConnectionString { get; set; } = null!;
    public string ChannelPrefix { get; set; } = null!;
}

public class ReplicatedLruCacheOptions
{
    public string AppId { get; set; } = null!;
    public RedisSyncBusOptions RedisSyncBus { get; set; } = new();
}