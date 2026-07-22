using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Ports;

/// <summary>Mode configuration for an isolated Runner one-shot job.</summary>
public sealed record RunnerJobConfiguration(
    RuntimeProvider Provider,
    string Image,
    IReadOnlyList<string>? Command = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    int TimeoutSeconds = 60);
