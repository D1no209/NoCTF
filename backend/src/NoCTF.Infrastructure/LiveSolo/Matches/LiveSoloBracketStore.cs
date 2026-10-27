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
        var matches = LiveSoloTournamentPolicy.Generate(competition.Id, command.TeamIds, configuration, 1, command.Now);
        var generation = (await db.LiveSoloMatches.IgnoreQueryFilters().Where(x => x.CompetitionId == competition.Id)
            .Select(x => (int?)x.BracketGeneration).MaxAsync(ct) ?? -1) + 1;
        foreach (var match in matches) match.BracketGeneration = generation;
        db.LiveSoloMatches.AddRange(matches);
        foreach (var match in matches.Where(x => x.State == LiveSoloMatchState.Preparing))
            db.LiveSoloActiveTeamSlots.AddRange(match.Slots.Select(x => new LiveSoloActiveTeamSlot { CompetitionId = competition.Id, MatchId = match.Id, TeamId = x.TeamId!.Value }));
        competition.ConcurrencyStamp = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        return new(await BracketAsync(competition.Id, competition.ConcurrencyStamp, configuration, ct));
    }, () => new(null, LiveSoloFailure.Conflict), ct);

    public async Task<LiveSoloBracketView?> ReadAsync(Guid competitionId, Guid actorId, CancellationToken ct)
    {
        if (!await ActiveAsync(actorId, ct) || !await authorizer.CanObserveAsync(actorId, competitionId, ct)) return null;
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == competitionId, ct);
        return competition?.ModeConfiguration is LiveSoloCompetitionModeConfiguration config
            ? await BracketAsync(competitionId, competition.ConcurrencyStamp, config, ct) : null;
    }
    private async Task<LiveSoloBracketView> BracketAsync(Guid competitionId, Guid stamp, LiveSoloCompetitionModeConfiguration configuration, CancellationToken ct)
    {
        var matches = await db.LiveSoloMatches.AsNoTracking().Include(x => x.Slots).Include(x => x.Roster)
            .Where(x => x.CompetitionId == competitionId && x.SupersededAt == null).OrderBy(x => x.TournamentStage).ThenBy(x => x.Lane).ThenBy(x => x.Stage).ThenBy(x => x.Position).ToArrayAsync(ct);
        var teamIds = matches.SelectMany(x => x.Slots).Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).Distinct().ToArray();
        var names = await db.Teams.IgnoreQueryFilters().Where(x => teamIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var avatars = await db.Teams.IgnoreQueryFilters().Where(x => teamIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.AvatarFileId, ct);
        var stages = LiveSoloTournamentPolicy.Stages(configuration).Select((rules, index) => {
            var stageMatches = matches.Where(x => x.TournamentStage == index + 1).ToArray();
            var complete = LiveSoloTournamentPolicy.Complete(stageMatches, rules);
            return new LiveSoloTournamentStageView(index + 1, rules, complete, LiveSoloTournamentPolicy.Standings(stageMatches, rules),
                complete ? LiveSoloTournamentPolicy.Qualifiers(stageMatches, rules) : []);
        }).ToArray();
        var last = stages[^1];
        var champion = last.Complete ? LiveSoloTournamentPolicy.Champion(matches.Where(x => x.TournamentStage == last.Number).ToArray(), last.Rules.Format) : null;
        var corrections = await CorrectionSourcesAsync(matches, ct);
        var matchIds = matches.Select(x => x.Id).ToArray();
        var withMedia = (await db.LiveSoloMediaSessions.AsNoTracking().Where(x => matchIds.Contains(x.MatchId))
            .Select(x => x.MatchId).Distinct().ToArrayAsync(ct)).ToHashSet();
        return new(competitionId, stamp, configuration.BracketFormat, matches.Select(x => new LiveSoloBracketMatchView(Map(x, names, corrections, withMedia.Contains(x.Id)) with {
            LeftAvatarFileId = x.Slots.Single(s => s.Side == LiveSoloSide.Left).TeamId is Guid left ? avatars.GetValueOrDefault(left) : null,
            RightAvatarFileId = x.Slots.Single(s => s.Side == LiveSoloSide.Right).TeamId is Guid right ? avatars.GetValueOrDefault(right) : null }, x.Lane, x.Stage, x.Position, x.Conditional,
            x.Slots.Select(s => new LiveSoloBracketSourceView(s.Side, s.Source, s.Seed, s.SourceMatchId, s.Resolved, s.TeamId)).ToArray())
            { TournamentStage = x.TournamentStage, SwissWins = x.SwissWins, SwissLosses = x.SwissLosses }).ToArray(),
            matches.Any(x => x.Slots.Any(s => s.Seed != null)) ? champion : null) { TournamentStages = stages };
    }
    private async Task AdvanceBracketAsync(LiveSoloMatch completed, DateTimeOffset now, CancellationToken ct)
    {
        var matches = await db.LiveSoloMatches.Include(x => x.Slots).Where(x => x.CompetitionId == completed.CompetitionId && x.SupersededAt == null).ToListAsync(ct);
        if (!matches.Any(x => x.Slots.Any(s => s.Seed != null))) return;
        var competition = await db.Competitions.SingleAsync(x => x.Id == completed.CompetitionId, ct);
        var configuration = (LiveSoloCompetitionModeConfiguration)competition.ModeConfiguration!;
        var stages = LiveSoloTournamentPolicy.Stages(configuration);
        var changed = LiveSoloBracketPolicy.Resolve(matches, now).ToHashSet();
        if (matches.All(x => x.PendingCorrectionId is null))
        {
            var generation = await db.LiveSoloMatches.Where(x => x.CompetitionId == completed.CompetitionId)
                .Select(x => x.BracketGeneration).MaxAsync(ct);
            for (var number = 1; number <= stages.Count; number++)
            {
                var rules = stages[number - 1];
                var stageMatches = matches.Where(x => x.TournamentStage == number).ToArray();
                IReadOnlyList<LiveSoloMatch> added = [];
                if (stageMatches.Length == 0)
                {
                    if (number == 1) break;
                    var previous = matches.Where(x => x.TournamentStage == number - 1).ToArray();
                    if (!LiveSoloTournamentPolicy.Complete(previous, stages[number - 2])) break;
                    var seeds = LiveSoloTournamentPolicy.Qualifiers(previous, stages[number - 2]);
                    if (seeds.Count == 0) break;
                    added = LiveSoloTournamentPolicy.Generate(competition.Id, seeds, configuration, number, now);
                }
                else if (rules.Format == LiveSoloBracketFormat.Swiss && !LiveSoloTournamentPolicy.Complete(stageMatches, rules))
                {
                    if (stageMatches.Any(x => x.State != LiveSoloMatchState.Completed)) break;
                    added = LiveSoloTournamentPolicy.SwissRound(competition.Id, LiveSoloTournamentPolicy.Seeds(stageMatches), stageMatches,
                        rules, configuration.StageRules, number, now);
                }
                foreach (var match in added) { match.BracketGeneration = generation + 1; changed.Add(match.Id); }
                db.LiveSoloMatches.AddRange(added); matches.AddRange(added);
                while (rules.Format == LiveSoloBracketFormat.Swiss && added.Count > 0 && added.All(x => x.State == LiveSoloMatchState.Completed)
                    && !LiveSoloTournamentPolicy.Complete(matches.Where(x => x.TournamentStage == number).ToArray(), rules))
                {
                    stageMatches = matches.Where(x => x.TournamentStage == number).ToArray();
                    added = LiveSoloTournamentPolicy.SwissRound(competition.Id, LiveSoloTournamentPolicy.Seeds(stageMatches), stageMatches,
                        rules, configuration.StageRules, number, now);
                    foreach (var match in added) { match.BracketGeneration = generation + 1; changed.Add(match.Id); }
                    db.LiveSoloMatches.AddRange(added); matches.AddRange(added);
                }
                if (!LiveSoloTournamentPolicy.Complete(matches.Where(x => x.TournamentStage == number).ToArray(), rules)) break;
            }
        }
        if (changed.Count != 0) competition.ConcurrencyStamp = Guid.NewGuid();
        foreach (var match in matches.Where(x => changed.Contains(x.Id) && x.State == LiveSoloMatchState.Preparing))
        {
            var teams = match.Slots.Select(x => x.TeamId!.Value).ToArray();
            if (await db.LiveSoloActiveTeamSlots.AnyAsync(x => x.CompetitionId == match.CompetitionId && teams.Contains(x.TeamId) && x.MatchId != match.Id, ct))
            { match.State = LiveSoloMatchState.AwaitingAdjudication; continue; }
            db.LiveSoloActiveTeamSlots.AddRange(teams.Select(team => new LiveSoloActiveTeamSlot { CompetitionId = match.CompetitionId, TeamId = team, MatchId = match.Id }));
        }
    }
}
