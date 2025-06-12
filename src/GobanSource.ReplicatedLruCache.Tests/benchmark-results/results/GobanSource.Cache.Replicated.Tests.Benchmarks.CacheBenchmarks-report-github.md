```

BenchmarkDotNet v0.14.0, macOS Sequoia 15.2 (24C101) [Darwin 24.2.0]
Apple M1, 1 CPU, 8 logical and 8 physical cores
.NET SDK 9.0.101
  [Host]     : .NET 9.0.0 (9.0.24.52809), Arm64 RyuJIT AdvSIMD
  DefaultJob : .NET 9.0.0 (9.0.24.52809), Arm64 RyuJIT AdvSIMD


```
| Method                     | Mean            | Error         | StdDev        | Median          | Ratio    | RatioSD | Gen0     | Gen1    | Allocated | Alloc Ratio |
|--------------------------- |----------------:|--------------:|--------------:|----------------:|---------:|--------:|---------:|--------:|----------:|------------:|
| &#39;LRU Set&#39;                  |     1,378.28 ns |     34.935 ns |    103.006 ns |     1,422.58 ns |     1.01 |    0.11 |   0.1183 |       - |     752 B |        1.00 |
| &#39;MemoryCache Set&#39;          |        54.72 ns |      0.746 ns |      0.623 ns |        54.52 ns |     0.04 |    0.00 |   0.0166 |       - |     104 B |        0.14 |
| &#39;LRU Get Existing&#39;         |       162.90 ns |      0.412 ns |      0.344 ns |       162.88 ns |     0.12 |    0.01 |   0.0153 |       - |      96 B |        0.13 |
| &#39;MemoryCache Get Existing&#39; |        21.56 ns |      0.203 ns |      0.170 ns |        21.50 ns |     0.02 |    0.00 |        - |       - |         - |        0.00 |
| &#39;LRU Get Missing&#39;          |        40.45 ns |      0.286 ns |      0.268 ns |        40.48 ns |     0.03 |    0.00 |        - |       - |         - |        0.00 |
| &#39;MemoryCache Get Missing&#39;  |        20.49 ns |      0.042 ns |      0.035 ns |        20.49 ns |     0.01 |    0.00 |        - |       - |         - |        0.00 |
| &#39;LRU Eviction&#39;             | 1,682,529.14 ns | 11,835.508 ns | 11,070.941 ns | 1,684,444.74 ns | 1,228.56 |  106.44 | 128.9063 | 62.5000 |  827208 B |    1,100.01 |
| &#39;MemoryCache Eviction&#39;     |    77,237.99 ns |    279.748 ns |    247.989 ns |    77,300.01 ns |    56.40 |    4.88 |  22.4609 |  6.7139 |  139654 B |      185.71 |
