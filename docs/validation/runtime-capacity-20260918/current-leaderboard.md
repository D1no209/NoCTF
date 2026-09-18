```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26220.9472)
Intel Core i9-14900HX 2.20GHz, 1 CPU, 32 logical and 24 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  Job-OHOJYQ : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

InvocationCount=512  IterationCount=12  LaunchCount=2
WarmupCount=12

```
| Method   | TeamCount | Mean       | Error    | StdDev   | Gen0     | Gen1     | Allocated |
|--------- |---------- |-----------:|---------:|---------:|---------:|---------:|----------:|
| **Combined** | **16**        |   **757.8 μs** | **15.30 μs** | **19.89 μs** | **130.8594** |  **78.1250** |   **2.37 MB** |
| **Combined** | **64**        | **3,050.5 μs** | **50.63 μs** | **62.18 μs** | **517.5781** | **390.6250** |    **9.3 MB** |
