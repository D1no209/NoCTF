using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Brackets;

public sealed record GenerateLiveSoloBracket(Guid CompetitionId, Guid ActorId, Guid ExpectedStamp, IReadOnlyList<Guid> TeamIds, DateTimeOffset Now);
public sealed record LiveSoloBracketSourceView(LiveSoloSide Side, LiveSoloSlotSource Source, int? Seed, Guid? SourceMatchId, bool Resolved, Guid? TeamId);
public sealed record LiveSoloBracketMatchView(LiveSoloMatchView Match, LiveSoloBracketLane Lane, int Stage, int Position, bool Conditional,
    IReadOnlyList<LiveSoloBracketSourceView> Sources);
public sealed record LiveSoloBracketView(Guid CompetitionId, Guid ConcurrencyStamp, LiveSoloBracketFormat Format,
    IReadOnlyList<LiveSoloBracketMatchView> Matches, Guid? ChampionTeamId);
public sealed record LiveSoloBracketResult(LiveSoloBracketView? Bracket, LiveSoloFailure? Failure = null);
public interface ILiveSoloBracketStore
{
    Task<LiveSoloBracketResult> GenerateAsync(GenerateLiveSoloBracket command, CancellationToken ct);
    Task<LiveSoloBracketView?> ReadAsync(Guid competitionId, Guid actorId, CancellationToken ct);
}
public sealed class ManageLiveSoloBracket(ILiveSoloBracketStore store)
{
    public Task<LiveSoloBracketResult> GenerateAsync(GenerateLiveSoloBracket command, CancellationToken ct) =>
        command.TeamIds.Count is < 2 or > 1024 || command.TeamIds.Contains(Guid.Empty) || command.TeamIds.Distinct().Count() != command.TeamIds.Count
        ? Task.FromResult(new LiveSoloBracketResult(null, LiveSoloFailure.InvalidConfiguration)) : store.GenerateAsync(command, ct);
    public Task<LiveSoloBracketView?> ReadAsync(Guid competitionId, Guid actorId, CancellationToken ct) => store.ReadAsync(competitionId, actorId, ct);
}
