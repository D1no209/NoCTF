using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Lifecycle;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

/// <summary>Reads balance inputs without building display history, actors, spectator frames or serialized snapshots.</summary>
internal sealed class HintBalanceReader(NoCtfDbContext db)
{
    public async Task<long> ReadAsync(GameplayFact fact, DateTimeOffset now, CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking().Where(item => item.Id == fact.CompetitionId)
            .Select(item => new { item.Mode, item.Status, item.StartAt, item.EndAt, item.TracksEnabled,
                item.Tracks, item.ModeConfiguration }).AsSplitQuery().SingleAsync(ct);
        var tracks = CompetitionTrackConfiguration.EffectiveFor(competition.Mode, competition.TracksEnabled, competition.Tracks);
        var teams = await db.Teams.AsNoTracking().Where(item => item.CompetitionId == fact.CompetitionId
                && item.RegistrationStatus == TeamRegistrationStatus.Approved)
            .Select(item => new { item.Id, item.Name, item.TrackKey, item.IsBanned, item.RegisteredAt }).ToArrayAsync(ct);
        var teamFacts = teams.Select(team =>
        {
            var track = CtfCompletionEligibility.Track(tracks, team.TrackKey);
            return new LeaderboardTeamFact(team.Id, team.Name, team.IsBanned, false, team.RegisteredAt,
                track.Key, track.EarnsScore, track.EarnsBlood, track.AffectsDynamicChallengeScore,
                track.VisibleOnLeaderboard, track.AffectsCompetitiveResults);
        }).ToArray();
        var scorePresets = competition.Mode == GameMode.Ctf
            ? await db.Set<NoCTF.Domain.Competitions.Scoring.CompetitionScorePreset>().AsNoTracking()
                .Where(item => item.CompetitionId == fact.CompetitionId).ToArrayAsync(ct) : [];
        var challengeRows = await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.CompetitionId == fact.CompetitionId)
            .Select(item => new { Fact = new LeaderboardChallengeFact(item.Id, "", "", false, item.Rules, item.Order,
                item.IsPublished, null,
                db.Set<CtfChallengeDefinition>().Where(definition => definition.ChallengeId == item.ChallengeId)
                    .Select(definition => definition.InteractionKind).FirstOrDefault(), null,
                new ChallengeTiming(item.AutoOpenAt, item.ScoringEndsAt, item.SubmissionDeadlineAt)),
                Tags = item.Tags.Select(tag => tag.Name).ToArray() })
            .AsSplitQuery().ToArrayAsync(ct);
        var challenges = challengeRows.Select(row => row.Fact with
        {
            ScorePreset = NoCTF.Domain.Competitions.Scoring.CompetitionScorePreset.Match(row.Tags, scorePresets)
        }).ToArray();
        var costs = await db.CompetitionChallenges.AsNoTracking().Where(item => item.CompetitionId == fact.CompetitionId)
            .SelectMany(item => item.Hints.Select(hint => new { hint.Id, hint.Cost })).ToDictionaryAsync(item => item.Id, item => item.Cost, ct);
        var lifecycle = await db.CompetitionEvents.AsNoTracking().Where(item => item.CompetitionId == fact.CompetitionId
                && item.Kind == CompetitionEventKind.CompetitionLifecycleChanged && item.OccurredAt <= now)
            .OrderBy(item => item.OccurredAt).ThenBy(item => item.Id)
            .Select(item => new CompetitionLifecycleTransition { Id = item.Id, CompetitionId = item.CompetitionId,
                From = item.PreviousCompetitionStatus!.Value, To = item.CompetitionStatus!.Value, OccurredAt = item.OccurredAt })
            .ToArrayAsync(ct);
        var status = lifecycle.Length == 0 ? competition.Status : lifecycle[^1].To;
        if (competition.Mode == GameMode.Ctf)
        {
            var window = CompetitionOfficialWindow.Resolve(competition.StartAt, competition.EndAt, lifecycle);
            teamFacts = teamFacts.Where(team => team.RegisteredAt < window.EndAt).ToArray();
        }
        var rows = await LeaderboardFactProjectionReader.ReadAsync(db, fact.CompetitionId, competition.Mode, status,
            competition.ModeConfiguration, competition.StartAt, competition.EndAt, lifecycle, now, costs, teamFacts,
            null, null, challenges, ct, scoringTeamId: fact.TeamId, excludedFactId: fact.Id);
        var unlocks = await db.WriteUpUnlockReceipts.AsNoTracking().Where(item => item.CompetitionId == fact.CompetitionId
                && (competition.Mode == GameMode.Ctf || item.TeamId == fact.TeamId) && item.UnlockedAt <= now)
            .Select(item => new LeaderboardWriteUpUnlock(item.GameplayFactId, item.TeamId, item.CompetitionChallengeId,
                item.DeductionPercent, item.UnlockedAt)).ToArrayAsync(ct);
        var theory = await NoCTF.Infrastructure.Theory.Scoring.TheoryScoreReader.ReadAsync(db, fact.CompetitionId, now, ct, fact.TeamId);
        var floor = competition.Mode == GameMode.Awd
            ? await db.AwdRoundRules.AsNoTracking().Where(item => item.CompetitionId == fact.CompetitionId && item.EffectiveAt <= now)
                .OrderByDescending(item => item.EffectiveAt).ThenByDescending(item => item.Revision)
                .Select(item => item.Options.MinimumTotalScore).FirstOrDefaultAsync(ct) : null;
        var input = new LeaderboardProjectionInput(fact.CompetitionId, competition.Mode, teamFacts, rows.Aggregate,
            challenges, competition.ModeConfiguration, competition.StartAt, lifecycle, ProjectedAt: now,
            CompetitionStatus: status, AwdAggregates: rows.AwdAggregates, WriteUpUnlocks: unlocks,
            AwdRoundScores: rows.AwdRoundScores, AwdMinimumTotalScore: floor, TheoryScores: theory.Scores);
        return new TeamScoreCalculator(new LeaderboardProjectorCatalog()).Calculate(input, fact.TeamId!.Value);
    }
}
