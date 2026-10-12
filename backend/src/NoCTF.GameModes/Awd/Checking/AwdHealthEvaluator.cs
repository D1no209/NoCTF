using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.GameModes.Awd.Checking;

/// <summary>Classifies evidence only; SLA ratios and attack results are separate policies.</summary>
public static class AwdHealthEvaluator
{
    public static (AwdHealthConclusion Conclusion, AwdHealthEvidence? Evidence) ValidateManifest(
        AwdProbe connection, AwdProbe functionality,
        IReadOnlyList<AwdExpectedFlag> expected, IReadOnlyList<AwdReportedFlag> reported)
    {
        if (expected.Select(flag => flag.FlagId).Distinct().Count() != expected.Count
            || reported.Select(flag => flag.FlagId).Distinct().Count() != reported.Count
            || expected.Count != reported.Count
            || !expected.Select(flag => flag.FlagId).ToHashSet().SetEquals(reported.Select(flag => flag.FlagId)))
            return (new(AwdHealth.InternalError, AwdCheckReason.InvalidEvidence, null, expected.Count, 0), null);
        var byId = reported.ToDictionary(flag => flag.FlagId);
        if (expected.Any(flag => flag.RequiresWrite && byId[flag.FlagId].Write.Outcome == AwdProbeOutcome.NotRequired)
            || !Valid(connection) || !Valid(functionality)
            || reported.Any(flag => !Valid(flag.Write) || !Valid(flag.Read)))
            return (new(AwdHealth.InternalError, AwdCheckReason.InvalidEvidence, null, expected.Count, 0), null);
        var evidence = new AwdHealthEvidence(connection, functionality, expected.Select(flag =>
            new AwdFlagObservation(flag.FlagId, flag.Historical, flag.Obligation,
                flag.RequiresWrite ? byId[flag.FlagId].Write : new(AwdProbeOutcome.NotRequired), byId[flag.FlagId].Read)).ToArray());
        return (Evaluate(evidence), evidence);
    }

    private static bool Valid(AwdProbe probe) => Enum.IsDefined(probe.Outcome) && Enum.IsDefined(probe.Reason)
        && (probe.Outcome switch
        {
            AwdProbeOutcome.Passed or AwdProbeOutcome.NotRequired => probe.Reason == AwdCheckReason.None,
            AwdProbeOutcome.TargetFailure => probe.Reason is AwdCheckReason.ConnectionRefused or AwdCheckReason.ConnectTimeout
                or AwdCheckReason.RequestTimeout or AwdCheckReason.FunctionalCheckFailed or AwdCheckReason.FlagStoreFailed
                or AwdCheckReason.CurrentFlagMissing or AwdCheckReason.HistoricalFlagMissing,
            AwdProbeOutcome.PlatformFailure => IsPlatformReason(probe.Reason),
            _ => true
        });

    private static bool IsPlatformReason(AwdCheckReason reason) => reason is AwdCheckReason.CheckerCrashed
        or AwdCheckReason.CheckerTimedOut or AwdCheckReason.TaskNotExecuted or AwdCheckReason.CallbackMissing
        or AwdCheckReason.InfrastructureFailure or AwdCheckReason.IncompleteEvidence or AwdCheckReason.InvalidEvidence
        or AwdCheckReason.RuntimeSuperseded or AwdCheckReason.LateReceipt;

    public static AwdHealthConclusion Evaluate(AwdHealthEvidence evidence)
    {
        var required = evidence.Flags.Where(flag => flag.Obligation == AwdFlagObligation.PlayerRequired).ToArray();
        var readable = required.Count(flag => flag.Read.Outcome == AwdProbeOutcome.Passed);
        var targetFailure = TargetFailure(evidence, required);
        var probes = new[] { evidence.Connection, evidence.Functionality }
            .Concat(required.SelectMany(flag => flag.Historical
                ? new[] { flag.Read }
                : new[] { flag.Write, flag.Read }));
        // Preserve a proven target failure even when another required probe was lost to the platform.
        var platformFailure = probes.FirstOrDefault(probe => probe.Outcome == AwdProbeOutcome.PlatformFailure
            || probe.Outcome == AwdProbeOutcome.Unknown && IsPlatformReason(probe.Reason));
        if (platformFailure is not null)
            return new(AwdHealth.InternalError, platformFailure.Reason, targetFailure, required.Length, readable);

        // A pending platform delivery is independent of a proven target failure.
        // Neither failure discharges the unresolved evidence from another store.
        if (evidence.Flags.Any(flag => flag.Obligation == AwdFlagObligation.PlatformPending))
            return Incomplete(targetFailure, required.Length, readable);

        if (evidence.Connection.Outcome == AwdProbeOutcome.TargetFailure)
            return new(AwdHealth.Offline, evidence.Connection.Reason, AwdHealth.Offline, required.Length, readable);
        if (evidence.Connection.Outcome != AwdProbeOutcome.Passed)
            return Incomplete(targetFailure, required.Length, readable);

        if (required.Any(flag => flag.Read.Outcome is AwdProbeOutcome.Unknown or AwdProbeOutcome.NotRequired
                || !flag.Historical && flag.Write.Outcome == AwdProbeOutcome.Unknown))
            return Incomplete(targetFailure, required.Length, readable);

        if (evidence.Functionality.Outcome == AwdProbeOutcome.TargetFailure)
            return new(AwdHealth.Mumble, evidence.Functionality.Reason, AwdHealth.Mumble, required.Length, readable);
        var currentFailure = required.Where(flag => !flag.Historical)
            .SelectMany(flag => new[] { flag.Write, flag.Read })
            .FirstOrDefault(probe => probe.Outcome == AwdProbeOutcome.TargetFailure);
        if (currentFailure is not null)
            return new(AwdHealth.Mumble, currentFailure.Reason, AwdHealth.Mumble, required.Length, readable);

        if (evidence.Functionality.Outcome != AwdProbeOutcome.Passed
            || required.Any(flag => !flag.Historical
                && (flag.Read.Outcome != AwdProbeOutcome.Passed
                    || flag.Write.Outcome is not (AwdProbeOutcome.Passed or AwdProbeOutcome.NotRequired))))
            return Incomplete(targetFailure, required.Length, readable);

        // A missing current probe is not evidence that the service recovered.
        if (required.Any(flag => flag.Historical
            && flag.Read.Outcome is not (AwdProbeOutcome.Passed or AwdProbeOutcome.TargetFailure)))
            return Incomplete(targetFailure, required.Length, readable);
        if (required.Any(flag => flag.Historical && flag.Read.Outcome == AwdProbeOutcome.TargetFailure))
            return new(AwdHealth.Recovering, AwdCheckReason.HistoricalFlagMissing, AwdHealth.Recovering, required.Length, readable);
        return new(AwdHealth.Ok, AwdCheckReason.None, null, required.Length, readable);
    }

    private static AwdHealth? TargetFailure(AwdHealthEvidence evidence, AwdFlagObservation[] required)
    {
        if (evidence.Connection.Outcome == AwdProbeOutcome.TargetFailure) return AwdHealth.Offline;
        if (evidence.Connection.Outcome == AwdProbeOutcome.Passed
            && (evidence.Functionality.Outcome == AwdProbeOutcome.TargetFailure
                || required.Any(flag => !flag.Historical
                    && (flag.Write.Outcome == AwdProbeOutcome.TargetFailure || flag.Read.Outcome == AwdProbeOutcome.TargetFailure))))
            return AwdHealth.Mumble;
        return null;
    }

    private static AwdHealthConclusion Incomplete(AwdHealth? known, int required, int readable) =>
        new(AwdHealth.InternalError, AwdCheckReason.IncompleteEvidence, known, required, readable);
}
