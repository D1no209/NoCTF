using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Media;

public sealed record LiveSoloProgramQuestionView(Guid Id, Guid CompetitionChallengeId, int Position, string Title, DateTimeOffset OpenedAt);
public sealed record LiveSoloProgramStateView(DateTimeOffset AsOf, LiveSoloMatchState MatchState, int RequiredWins,
    int LeftWins, int RightWins, Guid? LeftTeamId, Guid? RightTeamId, string? LeftTeamName, string? RightTeamName,
    Guid? RoundId, int? RoundNumber, LiveSoloRoundState? RoundState, long? TimelineRevision, long ActiveElapsedMilliseconds,
    int? LimitSeconds, bool Paused, IReadOnlyList<LiveSoloProgramQuestionView> Questions);
public sealed record LiveSoloProgramSegmentView(Guid Id, long Sequence, TimeSpan Duration, LiveSoloProgramStateView State);
public sealed record LiveSoloProgramView(Guid ProgramCaptureId, int DelaySeconds, LiveSoloProgramStateView State,
    IReadOnlyList<LiveSoloProgramSegmentView> Segments, bool Ended);
public sealed record LiveSoloProgramContent(Stream Content, string ContentType);
public interface ILiveSoloProgramReader
{
    Task<LiveSoloProgramView?> ReadAsync(Guid competitionId, Guid matchId, Guid actorId, Guid leaseId, CancellationToken cancellationToken);
    Task<LiveSoloProgramContent?> OpenSegmentAsync(Guid competitionId, Guid matchId, Guid segmentId, Guid actorId, Guid leaseId, CancellationToken cancellationToken);
}
