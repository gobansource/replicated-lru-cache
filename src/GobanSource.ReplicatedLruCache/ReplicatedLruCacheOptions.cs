using System;
namespace GobanSource.ReplicatedLruCache;

public class RedisSyncBusOptions
{
    [Obsolete("ConnectionString is ignored. Provide IConnectionMultiplexer instead.")]
    public string? ConnectionString { get; set; }
    public string ChannelPrefix { get; set; } = null!;
}

public class ReplicatedLruCacheOptions
{
    public string AppId { get; set; } = null!;
    public RedisSyncBusOptions RedisSyncBus { get; set; } = new();
}