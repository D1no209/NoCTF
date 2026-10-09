using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Brackets;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    public Task<LiveSoloBracketResult> GenerateAsync(GenerateLiveSoloBracket command, CancellationToken ct) => TransactionAsync(async () =>
    {
        if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct))
            return new LiveSoloBracketResult(null, LiveSoloFailure.Forbidden);
        var competition = await db.Competitions.SingleOrDefaultAsync(x => x.Id == command.CompetitionId, ct);
        if (competition?.ModeConfiguration is not LiveSoloCompetitionModeConfiguration configuration) return new(null, LiveSoloFailure.NotFound);
        if (competition.ConcurrencyStamp != command.ExpectedStamp) return new(null, LiveSoloFailure.Conflict);
        if (await db.LiveSoloMatches.AnyAsync(x => x.CompetitionId == competition.Id, ct)
            || await db.LiveSoloActiveTeamSlots.AnyAsync(x => x.CompetitionId == competition.Id, ct)) return new(null, LiveSoloFailure.Conflict);
        if (await db.Teams.CountAsync(x => command.TeamIds.Contains(x.Id) && x.CompetitionId == competition.Id
            && x.RegistrationStatus == TeamRegistrationStatus.Approved && !x.IsBanned, ct) != command.TeamIds.Count)
            return new(null, LiveSoloFailure.InvalidConfiguration);
        var matches = LiveSoloBracketPolicy.Generate(competition.Id, command.TeamIds, configuration, command.Now);
        db.LiveSoloMatches.AddRange(matches);
        foreach (var match in matches.Where(x => x.State == LiveSoloMatchState.Preparing))
            db.LiveSoloActiveTeamSlots.AddRange(match.Slots.Select(x => new LiveSoloActiveTeamSlot { CompetitionId = competition.Id, MatchId = match.Id, TeamId = x.TeamId!.Value }));
        competition.ConcurrencyStamp = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        return new(await BracketAsync(competition.Id, competition.ConcurrencyStamp, configuration.BracketFormat, ct));
    }, () => new(null, LiveSoloFailure.Conflict), ct);

    public async Task<LiveSoloBracketView?> ReadAsync(Guid competitionId, Guid actorId, CancellationToken ct)
    {
        if (!await ActiveAsync(actorId, ct) || !await authorizer.CanObserveAsync(actorId, competitionId, ct)) return null;
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == competitionId, ct);
        return competition?.ModeConfiguration is LiveSoloCompetitionModeConfiguration config
            ? await BracketAsync(competitionId, competition.ConcurrencyStamp, config.BracketFormat, ct) : null;
    }
    private async Task<LiveSoloBracketView> BracketAsync(Guid competitionId, Guid stamp, LiveSoloBracketFormat format, CancellationToken ct)
    {
        var matches = await db.LiveSoloMatches.AsNoTracking().Include(x => x.Slots).Include(x => x.Roster)
            .Where(x => x.CompetitionId == competitionId).OrderBy(x => x.Lane).ThenBy(x => x.Stage).ThenBy(x => x.Position).ToArrayAsync(ct);
        var teamIds = matches.SelectMany(x => x.Slots).Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).Distinct().ToArray();
        var names = await db.Teams.IgnoreQueryFilters().Where(x => teamIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var final = matches.Where(x => x.Lane == LiveSoloBracketLane.Winners).MaxBy(x => x.Stage);
        if (format == LiveSoloBracketFormat.DoubleElimination)
        {
            var reset = matches.FirstOrDefault(x => x.Lane == LiveSoloBracketLane.ResetFinal);
            final = reset?.State == LiveSoloMatchState.Completed ? reset : reset?.State == LiveSoloMatchState.Canceled
                ? matches.FirstOrDefault(x => x.Lane == LiveSoloBracketLane.GrandFinal) : null;
        }
        return new(competitionId, stamp, format, matches.Select(x => new LiveSoloBracketMatchView(Map(x, names), x.Lane, x.Stage, x.Position, x.Conditional,
            x.Slots.Select(s => new LiveSoloBracketSourceView(s.Side, s.Source, s.Seed, s.SourceMatchId, s.Resolved, s.TeamId)).ToArray())).ToArray(),
            matches.Any(x => x.Slots.Any(s => s.Seed != null)) && final?.State == LiveSoloMatchState.Completed ? final.WinnerTeamId : null);
    }
    private async Task AdvanceBracketAsync(LiveSoloMatch completed, DateTimeOffset now, CancellationToken ct)
    {
        var matches = await db.LiveSoloMatches.Include(x => x.Slots).Where(x => x.CompetitionId == completed.CompetitionId).ToArrayAsync(ct);
        if (!matches.Any(x => x.Slots.Any(s => s.SourceMatchId != null))) return;
        var changed = LiveSoloBracketPolicy.Resolve(matches, now);
        if (changed.Count != 0)
            (await db.Competitions.SingleAsync(x => x.Id == completed.CompetitionId, ct)).ConcurrencyStamp = Guid.NewGuid();
        foreach (var match in matches.Where(x => changed.Contains(x.Id) && x.State == LiveSoloMatchState.Preparing))
        {
            var teams = match.Slots.Select(x => x.TeamId!.Value).ToArray();
            if (await db.LiveSoloActiveTeamSlots.AnyAsync(x => x.CompetitionId == match.CompetitionId && teams.Contains(x.TeamId) && x.MatchId != match.Id, ct))
            { match.State = LiveSoloMatchState.AwaitingAdjudication; continue; }
            db.LiveSoloActiveTeamSlots.AddRange(teams.Select(team => new LiveSoloActiveTeamSlot { CompetitionId = match.CompetitionId, TeamId = team, MatchId = match.Id }));
        }
    }
}
