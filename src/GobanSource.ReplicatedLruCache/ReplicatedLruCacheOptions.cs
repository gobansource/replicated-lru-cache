using System;
using GobanSource.Bus.Redis;

namespace GobanSource.ReplicatedLruCache;


public class ReplicatedLruCacheOptions
{
    public RedisSyncBusOptions RedisSyncBus { get; set; } = new();
}