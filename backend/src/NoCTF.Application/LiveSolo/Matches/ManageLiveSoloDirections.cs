using NoCTF.Domain.LiveSolo;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.Application.LiveSolo.Matches;

public sealed record LiveSoloDirectionPresetInput(Guid? Id, string Name, string Direction, Guid QuestionGroupId);
public sealed record LiveSoloDirectionPresetView(Guid Id, string Name, string Direction, Guid QuestionGroupId, string QuestionGroupName);
public sealed record LiveSoloDirectionPresetResult(LiveSoloDirectionPresetView? Preset, LiveSoloFailure? Failure = null);
public sealed record LiveSoloDirectionPresetsResult(IReadOnlyList<LiveSoloDirectionPresetView> Presets, LiveSoloFailure? Failure = null);
public sealed record LiveSoloDirectionView(Guid Id, string Name, string Direction, int? RoundNumber, DateTimeOffset? DrawnAt,
    Guid? QuestionGroupId, bool Available);
public sealed record LiveSoloRescueView(Guid Id, Guid RoundId, Guid TeamId, Guid UserId, string UserName,
    string Reason, LiveSoloRescueState State, DateTimeOffset RequestedAt, Guid? JudgeUserId, string? DecisionReason);
public sealed record LiveSoloDirectionState(IReadOnlyList<LiveSoloDirectionView> Pool, IReadOnlyList<LiveSoloRescueView> Rescues,
    bool CanConfigure, bool CanDraw, bool CanRequestRescue, bool CanJudgeRescue, bool CanManagePresets);
public sealed record RequestLiveSoloRescue(Guid CompetitionId, Guid MatchId, Guid RoundId, Guid ActorId, Guid UserId, string Reason, DateTimeOffset Now);
public sealed record ResolveLiveSoloRescue(Guid CompetitionId, Guid MatchId, Guid RequestId, Guid ActorId, bool Approve, string Reason, DateTimeOffset Now);

public interface ILiveSoloDirectionStore
{
    Task<IReadOnlyList<LiveSoloDirectionPresetView>?> PresetsAsync(Guid competitionId, Guid actorId, CancellationToken ct);
    Task<LiveSoloDirectionPresetResult> SavePresetAsync(Guid competitionId, Guid actorId, LiveSoloDirectionPresetInput input, CancellationToken ct);
    Task<LiveSoloDirectionPresetsResult> SavePresetsAsync(Guid competitionId, Guid actorId, string direction, IReadOnlyList<Guid> questionGroupIds, CancellationToken ct);
    Task<LiveSoloFailure?> DeletePresetAsync(Guid competitionId, Guid presetId, Guid actorId, CancellationToken ct);
    Task<LiveSoloDirectionState?> DirectionsAsync(Guid competitionId, Guid matchId, Guid actorId, bool staff, CancellationToken ct);
    Task<LiveSoloMatchResult> ConfigurePoolAsync(Guid competitionId, Guid matchId, Guid actorId, IReadOnlyList<Guid> presetIds, CancellationToken ct);
    Task<LiveSoloMatchResult> DrawAsync(Guid competitionId, Guid matchId, Guid actorId, DateTimeOffset now, CancellationToken ct);
    Task<LiveSoloMatchResult> RequestRescueAsync(RequestLiveSoloRescue command, CancellationToken ct);
    Task<LiveSoloMatchResult> ResolveRescueAsync(ResolveLiveSoloRescue command, CancellationToken ct);
}

public static class LiveSoloDirectionPoolPolicy
{
    public static int RoundNumber(IReadOnlyList<LiveSoloRound> rounds)
    {
        var last = rounds.OrderBy(x => x.Number).ThenBy(x => x.Replay).LastOrDefault();
        return last?.State is LiveSoloRoundState.Canceled or LiveSoloRoundState.TimedOut ? last.Number : (last?.Number ?? 0) + 1;
    }
    public static bool CanConfigure(LiveSoloMatch match) => match.State is not (LiveSoloMatchState.Completed or LiveSoloMatchState.Canceled)
        && match.PendingCorrectionId == null && match.SupersededAt == null;
}
