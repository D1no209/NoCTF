namespace NoCTF.Application.Runtime.Configuration;

/// <summary>Mode configuration for an isolated Runner one-shot job.</summary>
public sealed record RunnerJobConfiguration(
    string Image,
    IReadOnlyList<string>? Command = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    int TimeoutSeconds = 60);
