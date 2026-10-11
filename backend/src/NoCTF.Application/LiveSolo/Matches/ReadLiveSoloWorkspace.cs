using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Matches;

public enum LiveSoloWorkspaceOperation : short { LockRoster, ConfirmReady, PrepareRound, StartCountdown, RetryPreparation, Pause, Resume, VoidRound, ForfeitMatch, CorrectResult, ManageQuestionPool, SubmitFlag }
public enum LiveSoloStartCondition : short { CompetitionNotRunning, LeftRosterUnlocked, RightRosterUnlocked, LeftNotReady, RightNotReady, NoPreparedRound, EnvironmentPreparing, EnvironmentFailed, MediaRequired, SelectionChanged, CorrectionPending }
public sealed record LiveSoloWorkspaceView(LiveSoloMatchView Match, LiveSoloRoundView? Round,
    IReadOnlyList<LiveSoloWorkspaceOperation> AllowedOperations, IReadOnlyList<LiveSoloStartCondition> MissingConditions,
    IReadOnlyList<LiveSoloQuestionView> Questions, IReadOnlyList<LiveSoloReviewQuestion> History, DateTimeOffset ServerTime,
    int? NextQuestionOffsetSeconds, bool Staff)
{
    public IReadOnlyList<LiveSoloPreparationView> Resources { get; init; } = [];
}
public sealed record LiveSoloPreparationView(Guid QuestionId, int Position, int OpenOffsetSeconds, DateTimeOffset? OpenedAt,
    LiveSoloQuestionReadiness Readiness, LiveSoloResourceFailure Failure, bool? EnvironmentRequired);
public interface ILiveSoloWorkspaceReader
{
    Task<LiveSoloWorkspaceView?> ReadAsync(Guid competitionId, Guid matchId, Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct);
}
