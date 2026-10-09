using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Adjudication;

public sealed record AdjudicateLiveSoloMatch(Guid CompetitionId, Guid MatchId, Guid ActorId,
    Guid ExpectedMatchStamp, Guid? ExpectedRoundId, Guid? ExpectedRoundStamp, long? ExpectedTimelineRevision,
    LiveSoloJudgeAction Action, Guid? ForfeitingTeamId, string Reason);
public sealed record LiveSoloAdjudicationView(Guid Id, Guid MatchId, Guid? RoundId, Guid ActorUserId, Guid? ForfeitingTeamId,
    LiveSoloJudgeAction Action, string Reason, DateTimeOffset OccurredAt, LiveSoloMatchState PreviousMatchState, LiveSoloMatchState MatchState,
    LiveSoloRoundState? PreviousRoundState, LiveSoloRoundState? RoundState, int PreviousLeftWins, int PreviousRightWins,
    int LeftWins, int RightWins, long? PreviousTimelineRevision, long? TimelineRevision);
public sealed record LiveSoloAdjudicationResult(LiveSoloMatchView? Match, LiveSoloRoundView? Round,
    LiveSoloAdjudicationView? Decision, LiveSoloFailure? Failure = null);
public interface ILiveSoloAdjudicationStore
{
    Task<LiveSoloAdjudicationResult> ApplyAsync(AdjudicateLiveSoloMatch command, CancellationToken ct);
    Task<IReadOnlyList<LiveSoloAdjudicationView>?> ReadAsync(Guid competitionId, Guid matchId, Guid actorId, CancellationToken ct);
}

public sealed class ManageLiveSoloAdjudication(ILiveSoloAdjudicationStore store)
{
    public Task<LiveSoloAdjudicationResult> ApplyAsync(AdjudicateLiveSoloMatch command, CancellationToken ct) =>
        !Enum.IsDefined(command.Action) || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > 4000
            || command.ExpectedMatchStamp == Guid.Empty || command.ExpectedRoundId == Guid.Empty
            || command.ExpectedRoundId is not null && (command.ExpectedRoundStamp is null || command.ExpectedRoundStamp == Guid.Empty
                || command.ExpectedTimelineRevision is null or < 0)
            || command.ExpectedRoundId is null && (command.ExpectedRoundStamp is not null || command.ExpectedTimelineRevision is not null)
            || (command.Action == LiveSoloJudgeAction.ForfeitMatch) != (command.ForfeitingTeamId is not null)
            || command.ForfeitingTeamId == Guid.Empty
        ? Task.FromResult(new LiveSoloAdjudicationResult(null, null, null, LiveSoloFailure.InvalidConfiguration))
        : store.ApplyAsync(command with { Reason = command.Reason.Trim() }, ct);
}
