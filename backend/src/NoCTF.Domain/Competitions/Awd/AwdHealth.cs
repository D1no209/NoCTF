namespace NoCTF.Domain.Competitions.Awd;

/// <summary>A conclusion about one AWD observation, independent of captures and execution.</summary>
public enum AwdHealth : short
{
    Ok,
    Mumble,
    Offline,
    Recovering,
    InternalError
}

public enum AwdCheckExecution : short { Pending, Running, Completed, FailedToDetermine }
public enum AwdSettlementState : short { Provisional, Final, PendingAdjudication, Exempted }
public enum AwdProbeOutcome : short { Unknown, Passed, TargetFailure, PlatformFailure, NotRequired }
public enum AwdFlagObligation : short { PlatformPending, PlayerRequired, Exempted }
public enum AwdCheckAdjudicationAction : short { AcceptEvidence, Exempt }

public enum AwdCheckReason : short
{
    None,
    ConnectionRefused,
    ConnectTimeout,
    RequestTimeout,
    FunctionalCheckFailed,
    FlagStoreFailed,
    CurrentFlagMissing,
    HistoricalFlagMissing,
    CheckerCrashed,
    CheckerTimedOut,
    TaskNotExecuted,
    CallbackMissing,
    InfrastructureFailure,
    IncompleteEvidence,
    InvalidEvidence,
    RuntimeSuperseded,
    LateReceipt
}

public sealed record AwdProbe(AwdProbeOutcome Outcome, AwdCheckReason Reason = AwdCheckReason.None);

/// <summary>Server-owned task scope, never accepted from the checker callback.</summary>
public sealed record AwdExpectedFlag(Guid FlagId, bool Historical, AwdFlagObligation Obligation, bool RequiresWrite);

/// <summary>The only per-flag evidence a checker can submit; it cannot choose its obligation or round.</summary>
public sealed record AwdReportedFlag(Guid FlagId, AwdProbe Write, AwdProbe Read,
    string? PrivateReadContext = null, string? PublicLocator = null)
{
    public override string ToString() => $"AwdReportedFlag {{ FlagId = {FlagId}, Write = {Write}, Read = {Read}, PrivateReadContext = [REDACTED], PublicLocator = [REDACTED] }}";
}

public sealed record AwdFlagObservation(
    Guid FlagId,
    bool Historical,
    AwdFlagObligation Obligation,
    AwdProbe Write,
    AwdProbe Read);

public sealed record AwdHealthEvidence(
    AwdProbe Connection,
    AwdProbe Functionality,
    IReadOnlyList<AwdFlagObservation> Flags);

public sealed record AwdHealthConclusion(
    AwdHealth Health,
    AwdCheckReason Reason,
    AwdHealth? KnownTargetFailure,
    int RequiredFlags,
    int ReadableFlags)
{
    public bool IsConclusive => Health != AwdHealth.InternalError;
}
