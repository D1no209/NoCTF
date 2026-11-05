using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Common;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.Application.Competitions.Awd;

public static class AwdCapabilityEvidence
{
    public static OperationResult<AwdCheckResult, CapabilityExecutionFailure> Map(AwdCapabilityCheckResult input,
        IReadOnlyList<AwdExpectedFlag> manifest)
    {
        var permitted = new HashSet<(CheckItemKind, Guid?)> { (CheckItemKind.Connection, null), (CheckItemKind.Functionality, null) };
        foreach (var flag in manifest)
        {
            if (flag.RequiresWrite) permitted.Add((CheckItemKind.FlagWrite, flag.FlagId));
            permitted.Add((flag.Historical ? CheckItemKind.HistoricalFlagRead : CheckItemKind.CurrentFlagRead, flag.FlagId));
        }
        if (input.Probes.Select(probe => (probe.Kind, probe.FlagId)).Distinct().Count() != input.Probes.Count
            || input.Probes.Any(probe => !permitted.Contains((probe.Kind, probe.FlagId)) || Probe(probe) is null)
            || input.FlagContexts.Any(context => input.Probes.All(probe => probe.FlagId != context.FlagId))
            || CheckFlagContextValidation.Validate(manifest.Select(flag => flag.FlagId).ToHashSet(), input.FlagContexts) is not null)
            return OperationResult<AwdCheckResult, CapabilityExecutionFailure>.Failure(CapabilityExecutionFailure.InvalidEvidence,
                "The Check evidence does not belong to this AWD task manifest.");
        var probes = input.Probes.ToDictionary(probe => (probe.Kind, probe.FlagId));
        var contexts = input.FlagContexts.ToDictionary(context => context.FlagId);
        var flags = manifest.Where(flag => probes.ContainsKey((CheckItemKind.FlagWrite, flag.FlagId))
                || probes.ContainsKey((flag.Historical ? CheckItemKind.HistoricalFlagRead : CheckItemKind.CurrentFlagRead, flag.FlagId)))
            .Select(flag =>
            {
                var context = contexts.GetValueOrDefault(flag.FlagId);
                return new AwdReportedFlag(flag.FlagId,
                    flag.RequiresWrite ? Read(CheckItemKind.FlagWrite, flag.FlagId) : new(AwdProbeOutcome.NotRequired),
                    Read(flag.Historical ? CheckItemKind.HistoricalFlagRead : CheckItemKind.CurrentFlagRead, flag.FlagId),
                    context?.PrivateReadContext, context?.PublicLocator);
            }).ToArray();
        return OperationResult<AwdCheckResult, CapabilityExecutionFailure>.Success(new(input.RuntimeInstanceId,
            input.GameplayFactId, input.AttemptId, Read(CheckItemKind.Connection, null), Read(CheckItemKind.Functionality, null),
            flags, input.ObservedAt, input.ReceivedAt));

        AwdProbe Read(CheckItemKind kind, Guid? flagId) => probes.TryGetValue((kind, flagId), out var value)
            ? Probe(value)! : new(AwdProbeOutcome.Unknown, AwdCheckReason.IncompleteEvidence);
    }

    private static AwdProbe? Probe(CheckProbeEvidence probe)
    {
        var outcome = probe.Result switch
        {
            CheckProbeResult.Passed => AwdProbeOutcome.Passed,
            CheckProbeResult.TargetFailure => AwdProbeOutcome.TargetFailure,
            CheckProbeResult.PlatformFailure => AwdProbeOutcome.PlatformFailure,
            CheckProbeResult.Unknown or CheckProbeResult.NotExecuted => AwdProbeOutcome.Unknown,
            CheckProbeResult.NotRequired => AwdProbeOutcome.NotRequired,
            _ => (AwdProbeOutcome?)null
        };
        var reason = probe.Reason switch
        {
            CheckFailureReason.None => AwdCheckReason.None,
            CheckFailureReason.ConnectionRefused => AwdCheckReason.ConnectionRefused,
            CheckFailureReason.ConnectTimeout => AwdCheckReason.ConnectTimeout,
            CheckFailureReason.RequestTimeout => AwdCheckReason.RequestTimeout,
            CheckFailureReason.FunctionalCheckFailed => AwdCheckReason.FunctionalCheckFailed,
            CheckFailureReason.FlagWriteFailed => AwdCheckReason.FlagStoreFailed,
            CheckFailureReason.FlagReadFailed => probe.Kind == CheckItemKind.HistoricalFlagRead ? AwdCheckReason.HistoricalFlagMissing : AwdCheckReason.CurrentFlagMissing,
            CheckFailureReason.CheckerCrashed => AwdCheckReason.CheckerCrashed,
            CheckFailureReason.CheckerTimedOut => AwdCheckReason.CheckerTimedOut,
            CheckFailureReason.CallbackMissing => probe.Result == CheckProbeResult.NotExecuted ? AwdCheckReason.TaskNotExecuted : AwdCheckReason.CallbackMissing,
            CheckFailureReason.IncompleteEvidence => AwdCheckReason.IncompleteEvidence,
            CheckFailureReason.InfrastructureFailure => AwdCheckReason.InfrastructureFailure,
            CheckFailureReason.InvalidEvidence => AwdCheckReason.InvalidEvidence,
            CheckFailureReason.TargetSuperseded => AwdCheckReason.RuntimeSuperseded,
            CheckFailureReason.PlayerMaintenance => AwdCheckReason.PlayerMaintenance,
            _ => (AwdCheckReason?)null
        };
        if (reason == AwdCheckReason.PlayerMaintenance || outcome is null || reason is null) return null;
        return new(outcome.Value, reason.Value);
    }
}
