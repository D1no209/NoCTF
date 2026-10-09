using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Matches;

public sealed record LiveSoloActor(Guid Id, bool Staff = false);
public sealed record LiveSoloRosterView(Guid TeamId, IReadOnlyList<Guid> UserIds, bool Locked, bool Ready);
public sealed record LiveSoloMatchView(Guid Id, Guid CompetitionId, LiveSoloMatchState State, Guid ConcurrencyStamp,
    int RequiredWins, int LeftWins, int RightWins, Guid? LeftTeamId, string? LeftTeamName, Guid? RightTeamId, string? RightTeamName,
    Guid? CurrentRoundId, Guid? WinnerTeamId, IReadOnlyList<LiveSoloRosterView> Rosters, Guid? PendingCorrectionId = null,
    Guid? ReplacementMatchId = null, Guid? PendingCorrectionMatchId = null);
public sealed record LiveSoloMatchResult(LiveSoloMatchView? Match, LiveSoloFailure? Failure = null);
public sealed record LiveSoloRoundView(Guid Id, Guid MatchId, int Number, int Replay, LiveSoloRoundState State,
    Guid ConcurrencyStamp, long TimelineRevision, DateTimeOffset? CountdownAt, DateTimeOffset? StartedAt,
    int LimitSeconds, long ActiveElapsedMilliseconds, bool Paused, Guid? WinnerTeamId, Guid? WinningGameplayFactId);
public sealed record LiveSoloRoundResult(LiveSoloRoundView? Round, LiveSoloFailure? Failure = null);
public sealed record LiveSoloQuestionGroupInput(Guid? Id, string Name, bool Reserve, int? LimitSeconds,
    IReadOnlyList<LiveSoloQuestionGroupEntry> Questions, Guid? ExpectedStamp);
public sealed record LiveSoloQuestionGroupEntry(Guid CompetitionChallengeId, int? OpenOffsetSeconds);
public sealed record LiveSoloQuestionGroupView(Guid Id, string Name, bool Reserve, int? LimitSeconds,
    Guid ConcurrencyStamp, IReadOnlyList<LiveSoloQuestionGroupEntry> Questions);
public sealed record LiveSoloQuestionGroupResult(LiveSoloQuestionGroupView? Group, LiveSoloFailure? Failure = null);
public sealed record CreateLiveSoloMatch(Guid CompetitionId, Guid ActorId, Guid LeftTeamId, Guid RightTeamId, int? RequiredWins, DateTimeOffset Now);
public sealed record SetLiveSoloRoster(Guid CompetitionId, Guid MatchId, Guid ActorId, Guid TeamId,
    Guid ExpectedStamp, IReadOnlyList<Guid> UserIds, DateTimeOffset Now);
public sealed record PrepareLiveSoloRound(Guid CompetitionId, Guid MatchId, Guid ActorId, Guid ExpectedStamp, Guid? GroupId, DateTimeOffset Now);
public sealed record StartLiveSoloCountdown(Guid CompetitionId, Guid MatchId, Guid RoundId, Guid ActorId, Guid ExpectedStamp, DateTimeOffset Now);
public sealed record LiveSoloAdmission(Guid CompetitionId, Guid MatchId, Guid RoundId, Guid QuestionId, Guid ActorId,
    string Flag, DateTimeOffset Now);
public sealed record LiveSoloAdmissionResult(Guid? GameplayFactId, long? Sequence, DateTimeOffset? AcceptedAt, LiveSoloFailure? Failure = null);
public sealed record LiveSoloQuestionView(Guid Id, Guid CompetitionChallengeId, int Position, string Title, string? Description,
    string Direction, IReadOnlyList<string> Tags, DateTimeOffset OpenedAt, Guid? RuntimeInstanceId);

public interface ILiveSoloMatchStore
{
    Task<LiveSoloMatchResult> CreateAsync(CreateLiveSoloMatch command, CancellationToken ct);
    Task<LiveSoloMatchView?> FindAsync(Guid competitionId, Guid matchId, Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyList<LiveSoloMatchView>?> ListAsync(Guid competitionId, Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct);
    Task<LiveSoloMatchResult> LockRosterAsync(SetLiveSoloRoster command, CancellationToken ct);
    Task<LiveSoloMatchResult> ConfirmReadyAsync(Guid competitionId, Guid matchId, Guid actorId, Guid expectedStamp, DateTimeOffset now, CancellationToken ct);
    Task<LiveSoloRoundResult> PrepareRoundAsync(PrepareLiveSoloRound command, CancellationToken ct);
    Task<LiveSoloRoundResult> StartCountdownAsync(StartLiveSoloCountdown command, CancellationToken ct);
    Task<LiveSoloRoundView?> FindRoundAsync(Guid competitionId, Guid matchId, Guid roundId, Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyList<LiveSoloQuestionView>?> QuestionsAsync(Guid competitionId, Guid matchId, Guid roundId, Guid actorId, DateTimeOffset now, CancellationToken ct);
    Task<LiveSoloAdmissionResult> AdmitAsync(LiveSoloAdmission command, CancellationToken ct);
    Task ResolveAsync(Guid roundId, DateTimeOffset now, CancellationToken ct);
    Task TickAsync(Guid roundId, long timelineRevision, DateTimeOffset now, CancellationToken ct);
    Task<LiveSoloQuestionGroupResult> SaveGroupAsync(Guid competitionId, Guid actorId, LiveSoloQuestionGroupInput input, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyList<LiveSoloQuestionGroupView>?> GroupsAsync(Guid competitionId, Guid actorId, CancellationToken ct);
}

public sealed class ManageLiveSoloMatches(ILiveSoloMatchStore store)
{
    public Task<LiveSoloMatchResult> ReadyAsync(Guid competitionId, Guid matchId, Guid actorId, Guid stamp, DateTimeOffset now, CancellationToken ct) =>
        store.ConfirmReadyAsync(competitionId, matchId, actorId, stamp, now, ct);
    public Task<LiveSoloRoundResult> PrepareAsync(PrepareLiveSoloRound command, CancellationToken ct) => store.PrepareRoundAsync(command, ct);
    public Task<LiveSoloRoundResult> CountdownAsync(StartLiveSoloCountdown command, CancellationToken ct) => store.StartCountdownAsync(command, ct);
    public Task<LiveSoloQuestionGroupResult> SaveGroupAsync(Guid competitionId, Guid actorId, LiveSoloQuestionGroupInput input, DateTimeOffset now, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 160 || input.Questions.Count is < 1 or > 64
            || input.Questions.Any(x => x.CompetitionChallengeId == Guid.Empty)
            || input.Questions.Select(x => x.CompetitionChallengeId).Distinct().Count() != input.Questions.Count
        ? Task.FromResult(new LiveSoloQuestionGroupResult(null, LiveSoloFailure.InvalidConfiguration))
        : store.SaveGroupAsync(competitionId, actorId, input, now, ct);
    public Task<LiveSoloMatchResult> CreateAsync(CreateLiveSoloMatch command, CancellationToken ct) => command.LeftTeamId == command.RightTeamId
        || command.RequiredWins is < 1 or > 1024 ? Task.FromResult(new LiveSoloMatchResult(null, LiveSoloFailure.InvalidConfiguration)) : store.CreateAsync(command, ct);
    public Task<LiveSoloMatchResult> RosterAsync(SetLiveSoloRoster command, CancellationToken ct) => command.UserIds.Count == 0
        || command.UserIds.Contains(Guid.Empty) || command.UserIds.Distinct().Count() != command.UserIds.Count
        ? Task.FromResult(new LiveSoloMatchResult(null, LiveSoloFailure.InvalidConfiguration)) : store.LockRosterAsync(command, ct);
    public Task<LiveSoloAdmissionResult> SubmitAsync(LiveSoloAdmission command, CancellationToken ct) => string.IsNullOrWhiteSpace(command.Flag)
        || command.Flag.Length > 4096 ? Task.FromResult(new LiveSoloAdmissionResult(null, null, null, LiveSoloFailure.InvalidConfiguration)) : store.AdmitAsync(command, ct);
}
