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
    BreakRequired,
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
        if (snapshot.CompetitionStatus != CompetitionStatus.Running)
            return snapshot.CompetitionStatus switch
            {
                CompetitionStatus.Paused => OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionPaused, "The competition is paused."),
                CompetitionStatus.Draft => OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionNotPublished, "The competition is not published."),
                CompetitionStatus.Published => OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionNotStarted, "The competition has not started."),
                CompetitionStatus.Finished => OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionFinished, "The competition has finished."),
                _ => OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionUnavailable, "The competition is not accepting submissions.")
            };
        if (receivedAt < snapshot.StartAt)
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionNotStarted, "The competition has not started.");
        if (receivedAt >= snapshot.EndAt)
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.CompetitionFinished, "The submission arrived after the deadline.");
        if (kind == GameplayFactKind.FixAttempt
            && rules.RequireBreakBeforeFix
            && !snapshot.HasCorrectBreak)
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.BreakRequired,
                "A correct Break submission is required before submitting a Fix.");
        var maxAttempts = kind is GameplayFactKind.FlagAttempt or GameplayFactKind.BreakAttempt ? rules.MaxFlagAttempts : rules.MaxFixAttempts;
        var acceptedAttempts = kind is GameplayFactKind.FlagAttempt or GameplayFactKind.BreakAttempt
            ? snapshot.AcceptedFlagAttempts
            : snapshot.AcceptedFixAttempts;
        if (maxAttempts is > 0 && acceptedAttempts >= maxAttempts)
            return OperationResult<GameplayFactAdmissionFailureCode>.Failure(GameplayFactAdmissionFailureCode.AttemptsExhausted, "The maximum number of accepted attempts has been reached.");
        return OperationResult<GameplayFactAdmissionFailureCode>.Success();
    }
}
