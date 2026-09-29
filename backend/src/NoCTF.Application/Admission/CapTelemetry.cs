namespace NoCTF.Application.Admission;

public enum CapTelemetryReadOutcome
{
    Available,
    Unconfigured,
    Unauthorized,
    Unavailable
}

public sealed record CapDailyTelemetry(
    long Verified,
    long Failed,
    long RateLimited,
    double AverageSolveDurationSeconds);

public sealed record CapTelemetryReadResult(
    CapTelemetryReadOutcome Outcome,
    CapDailyTelemetry? Snapshot = null);

public interface ICapTelemetryReader
{
    Task<CapTelemetryReadResult> ReadTodayAsync(
        CapHumanVerificationOptions options,
        CancellationToken cancellationToken);
}
