using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.GameplayFacts.Intake;

public enum GameplayFactAdmissionFailureCode
{
    ResourceDeleted,
    ChallengeUnavailable,
    TeamForbidden,
    TeamBanned,
    GameplayFactKindUnsupported,
    CompetitionPaused,
    CompetitionNotPublished,
    CompetitionNotStarted,
    CompetitionFinished,
    CompetitionUnavailable,
    RuntimeNotRunning,
    BreakRequired,
    AchievementAlreadySucceeded,
    AttemptsExhausted,
    FlagInvalid,
    FlagBatchNotSupported,
    GameplayFactScopeNotFound,
    GameplayFactConcurrency,
    PatchUploadNotFound
}

/// <summary>Applies transport-independent admission rules using the trusted receive time.</summary>
public static class GameplayFactAdmissionPolicy
{
    public static bool IsPracticeFlagAttempt(
        GameplayFactAdmissionSnapshot snapshot,
        GameplayFactKind kind,
        DateTimeOffset receivedAt) =>
        snapshot.Mode == GameMode.Ctf
        && kind == GameplayFactKind.FlagAttempt
        && snapshot.CompetitionStatus == CompetitionStatus.Finished
        && snapshot.PracticeModeEnabled
        && receivedAt >= (snapshot.OfficialEndAt ?? snapshot.EndAt);

    public static OperationResult<GameplayFactAdmissionFailureCode> Check(
        GameplayFactAdmissionSnapshot snapshot,
        GameplayFactKind kind,
        GameplayFactAdmissionRules rules,
        DateTimeOffset receivedAt)
    {
        if (snapshot.CompetitionDeleted || snapshot.ChallengeDeleted || snapshot.TeamDeleted)
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.ResourceDeleted, "The competition, challenge, or team is deleted.");
        if (!snapshot.ChallengePublished)
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.ChallengeUnavailable, "The challenge is not published.");
        if (!snapshot.TeamApproved || !snapshot.UserBelongsToTeam)
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.TeamForbidden, "The user cannot submit for this team.");
        if (snapshot.TeamBanned)
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.TeamBanned, "The team is banned.");
        if (kind is GameplayFactKind.FlagAttempt or GameplayFactKind.BreakAttempt && !rules.AllowsFlag
            || kind == GameplayFactKind.FixAttempt && !rules.AllowsFix)
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.GameplayFactKindUnsupported, "This game mode does not accept this gameplay fact kind.");
        var practice = IsPracticeFlagAttempt(snapshot, kind, receivedAt);
        if (!practice && snapshot.CompetitionStatus != CompetitionStatus.Running)
            return snapshot.CompetitionStatus switch
            {
                CompetitionStatus.Paused => OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionPaused, "The competition is paused."),
                CompetitionStatus.Draft => OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionNotPublished, "The competition is not published."),
                CompetitionStatus.Published => OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionNotStarted, "The competition has not started."),
                CompetitionStatus.Finished => OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionFinished, "The competition has finished."),
                _ => OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionUnavailable, "The competition is not accepting submissions.")
            };
        if (!practice)
        {
            if (receivedAt < snapshot.StartAt)
                return OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionNotStarted, "The competition has not started.");
            if (receivedAt >= (snapshot.OfficialEndAt ?? snapshot.EndAt))
                return OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionFinished, "The submission arrived after the deadline.");
        }
        else if (snapshot.PracticeRuntimeState == PracticeRuntimeAdmissionState.Unsupported)
        {
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.ChallengeUnavailable,
                "The challenge Runtime is not supported in practice mode.");
        }
        else if (snapshot.PracticeRuntimeState == PracticeRuntimeAdmissionState.NotRunning)
        {
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.RuntimeNotRunning,
                "Start the practice Runtime and wait until it is running before submitting a Flag.");
        }
        if (kind == GameplayFactKind.FixAttempt
            && rules.RequireBreakBeforeFix
            && !snapshot.HasCorrectBreak)
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.BreakRequired,
                "A correct Break submission is required before submitting a Fix.");
        if (snapshot.Mode == GameMode.Awdp
            && (kind == GameplayFactKind.BreakAttempt && snapshot.HasCorrectBreak
                || kind == GameplayFactKind.FixAttempt && snapshot.HasCorrectFix))
        {
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.AchievementAlreadySucceeded,
                "This AWDP attack or defense achievement has already succeeded.");
        }
        var maxAttempts = kind is GameplayFactKind.FlagAttempt or GameplayFactKind.BreakAttempt ? rules.MaxFlagAttempts : rules.MaxFixAttempts;
        var acceptedAttempts = kind is GameplayFactKind.FlagAttempt or GameplayFactKind.BreakAttempt
            ? snapshot.AcceptedFlagAttempts
            : snapshot.AcceptedFixAttempts;
        if (!practice && maxAttempts is > 0 && acceptedAttempts >= maxAttempts)
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.AttemptsExhausted, "The maximum number of accepted attempts has been reached.");
        return OperationResult<GameplayFactAdmissionFailureCode>.Success();
    }
}
