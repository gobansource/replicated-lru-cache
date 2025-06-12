using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using GobanSource.ReplicatedLruCache.Tests.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        var config = DefaultConfig.Instance
            .AddExporter(HtmlExporter.Default)
            .WithArtifactsPath("./benchmark-results");

        BenchmarkRunner.Run<CacheBenchmarks>(config);
    }
}