using NoCTF.Domain.Platform;

namespace NoCTF.Application.Admission;

public enum HumanVerificationMonitoringState
{
    NotApplicable,
    Pending,
    Healthy,
    Misconfigured,
    Unavailable
}

public sealed record HumanVerificationMonitoringSnapshot(
    HumanVerificationProvider Provider,
    bool Enabled,
    HumanVerificationMonitoringState State,
    DateTimeOffset? CheckedAt,
    long? LatencyMilliseconds)
{
    public static HumanVerificationMonitoringSnapshot NotApplicable(
        DateTimeOffset? checkedAt = null) => new(
        HumanVerificationProvider.None,
        false,
        HumanVerificationMonitoringState.NotApplicable,
        checkedAt,
        null);
}

public interface IHumanVerificationMonitoringReader
{
    Task<HumanVerificationMonitoringSnapshot> ReadAsync(
        CancellationToken cancellationToken);
}
