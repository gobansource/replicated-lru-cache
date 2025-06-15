using System;
using GobanSource.Bus.Redis;

namespace GobanSource.ReplicatedLruCache;


public class ReplicatedLruCacheOptions
{
    public string AppId { get; set; } = null!;
    public RedisSyncBusOptions RedisSyncBus { get; set; } = new();
}