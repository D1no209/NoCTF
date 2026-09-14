# CTF PatchVerification alpha.1 性能记录

测试时间：2026-09-15。基线为 `main@3ee07066c`，候选版本为
`codex/ctf-patch-alpha` 的 `0.2.1-alpha.1` 工作树。两次均在同一台 i9-14900HX、
Windows 11、.NET SDK 10.0.300、.NET 10.0.8 和“高性能”电源计划下运行
BenchmarkDotNet ShortRun（1 次启动、3 次预热、3 次测量）。原始结果保存在工作机的
`%TEMP%\noctf-performance\ctf-patch-alpha`，不提交生成物。

## Flag-only CTF 回归

| 队伍 | 基线 Mean | 候选 Mean | Mean 变化 | 基线 Allocated | 候选 Allocated | 分配变化 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 16 | 1,157.3 μs | 785.2 μs | -32.15% | 2.35 MB | 2.37 MB | +0.85% |
| 64 | 4,835.0 μs | 3,328.3 μs | -31.16% | 9.22 MB | 9.30 MB | +0.87% |

候选版本的 Mean 和 Allocated 均满足“不回归超过 5%”门禁。ShortRun 的样本数较小，
时间下降只说明本轮没有观察到回归，不将其解释为确定的性能优化幅度。分配量更稳定，
16/64 队的增幅都低于 1%。

## 混合 Flag/Patch 语料

语料包含 12 道题，偶数题为 PatchVerification，奇数题为 FlagSubmission。每队每题包含
一次成功和三次聚合错误，并保留与 Flag-only 基准一致的提示与人工调整分布。

| 队伍 | Mean | StdDev | Allocated |
| ---: | ---: | ---: | ---: |
| 16 | 783.8 μs | 24.82 μs | 2.31 MB |
| 64 | 3,684.7 μs | 123.66 μs | 9.10 MB |

混合投影没有引入按题数据库读取；`interactionKind` 与题目规则一起进入现有排行榜输入，
热路径按题目字典完成一次枚举判定。
