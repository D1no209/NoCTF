using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Domain.LiveSolo;
using NoCTF.GameModes.LiveSolo.Configuration;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    public async Task<IReadOnlyList<LiveSoloQuestionCandidate>?> CandidatesAsync(Guid competitionId, Guid matchId, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        if (!await ActiveAsync(actorId, ct) || !await authorizer.CanObserveAsync(actorId, competitionId, ct)) return null;
        var match = await MatchAsync(competitionId, matchId, ct);
        if (match is null) return null;
        return await QuestionCandidatesAsync(match, now, ct);
    }

    private async Task<IReadOnlyList<LiveSoloQuestionCandidate>> QuestionCandidatesAsync(LiveSoloMatch match, DateTimeOffset now, CancellationToken ct)
    {
        var groups = await db.LiveSoloQuestionGroups.AsNoTracking().Include(x => x.Items).Where(x => x.CompetitionId == match.CompetitionId).OrderBy(x => x.Position).ToArrayAsync(ct);
        var config = await db.Set<LiveSoloCompetitionModeConfiguration>().AsNoTracking().SingleAsync(x => x.CompetitionId == match.CompetitionId, ct);
        var entries = await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompetitionId == match.CompetitionId).ToArrayAsync(ct);
        var templates = await db.Challenges.IgnoreQueryFilters().AsNoTracking().Where(x => entries.Select(e => e.ChallengeId).Contains(x.Id)).AsSplitQuery().ToDictionaryAsync(x => x.Id, ct);
        var sources = await db.LiveSoloChallengeSources.AsNoTracking().Where(x => templates.Keys.Contains(x.ChallengeId)).ToDictionaryAsync(x => x.ChallengeId, x => x.CanonicalChallengeId, ct);
        Guid Canonical(Guid template) => sources.GetValueOrDefault(template, template);
        var teamIds = match.Slots.Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).ToArray(); var userIds = match.Roster.Select(x => x.UserId).ToArray();
        var history = await db.LiveSoloRoundQuestions.AsNoTracking().Where(x => x.OpenedAt != null)
            .Join(db.LiveSoloRounds.AsNoTracking(), x => x.RoundId, x => x.Id, (q, r) => new { Question = q, Round = r })
            .Join(db.LiveSoloMatches.AsNoTracking(), x => x.Round.MatchId, x => x.Id, (x, m) => new { x.Question, x.Round, Match = m })
            .Where(x => x.Match.CompetitionId == match.CompetitionId)
            .Select(x => new { x.Question.CompetitionChallengeId, x.Round.QuestionGroupId, x.Round.MatchId,
                SeenByTeam = x.Match.Slots.Any(s => s.TeamId != null && teamIds.Contains(s.TeamId.Value)),
                SeenByMember = x.Match.Roster.Any(r => userIds.Contains(r.UserId)) }).ToArrayAsync(ct);
        var active = await db.LiveSoloRoundQuestions.AsNoTracking()
            .Join(db.LiveSoloRounds.AsNoTracking(), x => x.RoundId, x => x.Id, (q, r) => new { Question = q, Round = r })
            .Where(x => (x.Round.State == LiveSoloRoundState.Preparing || x.Round.State == LiveSoloRoundState.Countdown || x.Round.State == LiveSoloRoundState.Running || x.Round.State == LiveSoloRoundState.ConfirmingResult)
                && x.Round.MatchId != match.Id && db.LiveSoloMatches.Any(m => m.Id == x.Round.MatchId && m.CompetitionId == match.CompetitionId))
            .Select(x => x.Question.CompetitionChallengeId).Distinct().ToArrayAsync(ct);
        await NoCTF.Infrastructure.LiveSolo.Questions.LiveSoloPublicExposure.RememberAsync(db, match.CompetitionId, now, ct);
        var exposed = await db.LiveSoloQuestionExposures.AsNoTracking().Where(x => x.PublicAt <= now).Select(x => x.CanonicalChallengeId).Distinct().ToArrayAsync(ct);
        var seenTeams = entries.Where(x => history.Any(h => h.CompetitionChallengeId == x.Id && h.SeenByTeam)).Select(x => Canonical(x.ChallengeId)).ToHashSet();
        var seenMembers = entries.Where(x => history.Any(h => h.CompetitionChallengeId == x.Id && h.SeenByMember)).Select(x => Canonical(x.ChallengeId)).ToHashSet();
        return groups.Select(group =>
        {
            var ordered = group.Items.OrderBy(x => x.Position).ToArray();
            var materials = ordered.Select(item =>
            {
                var entry = entries.SingleOrDefault(x => x.Id == item.CompetitionChallengeId);
                if (entry is null || !templates.TryGetValue(entry.ChallengeId, out var template))
                    return new LiveSoloSelectionMaterial(item.CompetitionChallengeId, null, Guid.Empty, Guid.Empty, false, false, false, false, false, false, false);
                var canonical = Canonical(template.Id);
                return new LiveSoloSelectionMaterial(entry.Id, canonical, entry.ConcurrencyStamp, template.ConcurrencyStamp, true,
                    entry.DeletedAt != null || template.DeletedAt != null,
                    template.Definition is LiveSoloChallengeDefinition definition && LiveSoloConfigurationValidator.ValidateDefinition(definition).Count == 0,
                    active.Contains(entry.Id), seenTeams.Contains(canonical), seenMembers.Contains(canonical), exposed.Contains(canonical));
            }).ToArray();
            return LiveSoloQuestionSelection.Evaluate(group.Id, group.Name, group.Reserve, group.Position, group.ConcurrencyStamp, materials,
                ordered.Select(x => new LiveSoloQuestionGroupEntry(x.CompetitionChallengeId, x.OpenOffsetSeconds)).ToArray(),
                LiveSoloConfigurationValidator.ValidateGroup(group, config.RoundLimitSeconds, config.QuestionIntervalSeconds).Count == 0,
                history.Any(x => x.MatchId == match.Id && x.QuestionGroupId == group.Id), config.QuestionIntervalSeconds, group.RoundLimitSeconds ?? config.RoundLimitSeconds);
        }).ToArray();
    }
}
