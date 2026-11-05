using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Infrastructure.Competitions.Awd;

public static class AwdCheckCapabilityEvidence
{
    public static void Apply(GameplayFact fact, DateTimeOffset observedAt)
    {
        var details = fact.Awd ?? throw new InvalidOperationException("An AWD check requires its server-owned manifest.");
        var probes = new List<CheckProbe>();
        void Add(CheckItemKind kind, Guid? flag, AwdProbeOutcome result, AwdCheckReason reason) => probes.Add(new()
        {
            GameplayFactId = fact.Id, Position = probes.Count, Kind = kind, FlagId = flag,
            Result = result switch
            {
                AwdProbeOutcome.Passed => CheckProbeResult.Passed, AwdProbeOutcome.TargetFailure => CheckProbeResult.TargetFailure,
                AwdProbeOutcome.PlatformFailure => CheckProbeResult.PlatformFailure, AwdProbeOutcome.NotRequired => CheckProbeResult.NotRequired,
                AwdProbeOutcome.Unknown => CheckProbeResult.Unknown, _ => throw new ArgumentOutOfRangeException(nameof(result))
            },
            Reason = Reason(reason)
        });
        Add(CheckItemKind.Connection, null, details.Connection, details.ConnectionReason);
        Add(CheckItemKind.Functionality, null, details.Functionality, details.FunctionalityReason);
        foreach (var flag in details.Flags.OrderBy(value => value.FlagId))
        {
            if (flag.RequiresWrite) Add(CheckItemKind.FlagWrite, flag.FlagId, flag.Write, flag.WriteReason);
            Add(flag.Historical ? CheckItemKind.HistoricalFlagRead : CheckItemKind.CurrentFlagRead, flag.FlagId, flag.Read, flag.ReadReason);
        }
        var outcome = fact.CapabilityOutcomes.OfType<CheckOutcome>().SingleOrDefault();
        if (outcome is null) { outcome = new() { GameplayFactId = fact.Id }; fact.CapabilityOutcomes.Add(outcome); }
        outcome.RuntimeInstanceId = details.RuntimeId; outcome.ObservedAt = observedAt;
        var completed = details.EvidenceComplete && probes.All(probe => probe.Result is CheckProbeResult.Passed or CheckProbeResult.TargetFailure or CheckProbeResult.NotRequired);
        outcome.ExecutionState = completed ? CapabilityExecutionState.Completed : CapabilityExecutionState.PlatformFailed;
        outcome.ExecutionFailure = completed ? null : CapabilityExecutionFailure.MissingEvidence;
        foreach (var probe in probes)
        {
            var current = outcome.Probes.SingleOrDefault(value => value.Position == probe.Position);
            if (current is null) outcome.Probes.Add(probe);
            else { current.Kind = probe.Kind; current.FlagId = probe.FlagId; current.Result = probe.Result; current.Reason = probe.Reason; }
        }
        outcome.Probes.RemoveAll(value => value.Position >= probes.Count);
    }

    private static CheckFailureReason Reason(AwdCheckReason reason) => reason switch
    {
        AwdCheckReason.None => CheckFailureReason.None,
        AwdCheckReason.ConnectionRefused => CheckFailureReason.ConnectionRefused,
        AwdCheckReason.ConnectTimeout => CheckFailureReason.ConnectTimeout,
        AwdCheckReason.RequestTimeout => CheckFailureReason.RequestTimeout,
        AwdCheckReason.FunctionalCheckFailed => CheckFailureReason.FunctionalCheckFailed,
        AwdCheckReason.FlagStoreFailed => CheckFailureReason.FlagWriteFailed,
        AwdCheckReason.CurrentFlagMissing or AwdCheckReason.HistoricalFlagMissing => CheckFailureReason.FlagReadFailed,
        AwdCheckReason.CheckerCrashed => CheckFailureReason.CheckerCrashed,
        AwdCheckReason.CheckerTimedOut => CheckFailureReason.CheckerTimedOut,
        AwdCheckReason.CallbackMissing or AwdCheckReason.TaskNotExecuted => CheckFailureReason.CallbackMissing,
        AwdCheckReason.RuntimeSuperseded => CheckFailureReason.TargetSuperseded,
        AwdCheckReason.PlayerMaintenance => CheckFailureReason.PlayerMaintenance,
        AwdCheckReason.InfrastructureFailure => CheckFailureReason.InfrastructureFailure,
        AwdCheckReason.IncompleteEvidence => CheckFailureReason.IncompleteEvidence,
        AwdCheckReason.InvalidEvidence or AwdCheckReason.LateReceipt => CheckFailureReason.InvalidEvidence,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };
}
