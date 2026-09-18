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
| **Combined** | **16**        |   **756.6 μs** | **12.32 μs** | **16.01 μs** | **128.9063** |  **93.7500** |   **2.32 MB** |
| **Combined** | **64**        | **3,042.8 μs** | **71.23 μs** | **90.08 μs** | **505.8594** | **386.7188** |   **9.11 MB** |
