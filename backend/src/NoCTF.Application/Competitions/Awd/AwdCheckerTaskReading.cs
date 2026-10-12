namespace NoCTF.Application.Competitions.Awd;

public sealed record AwdCheckerTask(Guid GameplayFactId, Guid AttemptId, DateTimeOffset Deadline,
    IReadOnlyList<AwdCheckerTaskFlag> Flags)
{
    public override string ToString() => $"AwdCheckerTask {{ GameplayFactId = {GameplayFactId}, AttemptId = {AttemptId}, Flags = [REDACTED] }}";
}

public sealed record AwdCheckerTaskFlag(Guid FlagId, Guid StoreId, string StoreName, string? TargetServiceName,
    string Flag, bool Historical, bool RequiresWrite, string? PrivateReadContext, string? PublicLocator)
{
    public override string ToString() => $"AwdCheckerTaskFlag {{ FlagId = {FlagId}, StoreId = {StoreId}, Flag = [REDACTED], PrivateReadContext = [REDACTED] }}";
}

public interface IAwdCheckerTaskReader
{
    Task<AwdCheckerTask?> ReadAsync(Guid runtimeId, Guid factId, Guid attemptId, DateTimeOffset at, CancellationToken ct);
}
