using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.Application.LiveSolo.Matches;

public sealed record DeleteLiveSoloMatch(Guid CompetitionId, Guid MatchId, Guid ActorId, DateTimeOffset Now);
public sealed record LiveSoloMatchDeletionResult(IReadOnlyList<Guid> MatchIds, LiveSoloFailure? Failure = null);
