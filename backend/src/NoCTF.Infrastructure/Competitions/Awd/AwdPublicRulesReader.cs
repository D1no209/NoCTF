using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

public sealed class AwdPublicRulesReader(NoCtfDbContext db) : IAwdPublicRulesReader
{
    public async Task<AwdPublicRulesView?> ReadAsync(Guid competitionId, Guid challengeId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, IsolationLevel.RepeatableRead, ct);
        var competition = await db.Competitions.AsNoTracking().Include(item => item.ModeConfiguration)
            .SingleOrDefaultAsync(item => item.Id == competitionId && item.Mode == GameMode.Awd && item.DeletedAt == null, ct);
        var challenge = await db.CompetitionChallenges.AsNoTracking().Include(item => item.Rules)
            .SingleOrDefaultAsync(item => item.CompetitionId == competitionId && item.Id == challengeId
                && item.IsPublished && item.DeletedAt == null, ct);
        if (competition?.ModeConfiguration is not AwdCompetitionModeConfiguration defaults
            || challenge?.Rules is not AwdCompetitionChallengeRules overrides) return null;
        var template = await db.Challenges.AsNoTracking().Include(item => item.Definition)
            .SingleAsync(item => item.Id == challenge.ChallengeId, ct);
        var deadline = await AwdCompetitionDeadline.ReadAsync(db, competitionId, competition.StartAt,
            competition.EndAt, defaults, now, ct);
        var round = deadline.Rounds.LastOrDefault();
        var issued = round is null ? [] : await db.AwdRoundRules.AsNoTracking()
            .Where(item => item.RoundId == round.Id && item.CompetitionChallengeId == challengeId)
            .OrderBy(item => item.Revision).ToArrayAsync(ct);
        var latest = round is null ? null : await db.Set<AwdScoringRuleChange>().AsNoTracking()
            .Where(item => item.CompetitionId == competitionId && item.CompetitionChallengeId == challengeId
                && item.FirstRound <= round.Number && item.LastRound >= round.Number && item.AppliedAt <= now)
            .OrderByDescending(item => item.Sequence).FirstOrDefaultAsync(ct);
        var resolved = AwdEffectiveRules.Resolve(defaults, overrides);
        var nextStores = ((AwdChallengeDefinition)template.Definition!).FlagStores.OrderBy(store => store.Position)
            .Select(store => { var rule = overrides.StoreRules.SingleOrDefault(item => item.StoreId == store.Id);
                return new AwdPublicStoreRule(store.Id, store.Name, rule?.Weight ?? 1m,
                    rule?.FlagsPerIssue ?? resolved.Options.FlagsPerStore); }).ToArray();
        var nextSnapshot = new AwdRoundRules { AttackRewardMode = resolved.AttackMode, AttackPoints = resolved.AttackPoints,
            VictimDefensePoolPoints = resolved.DefensePoints, ServiceHealthyPoints = resolved.SlaPoints,
            CheckerIntervalSeconds = resolved.CheckerIntervalSeconds, Options = resolved.Options };
        await transaction.CommitAsync(ct);
        return new(now, deadline.EndsAt, deadline.AcceptanceEndsAt, competition.Status == CompetitionStatus.Paused,
            competition.Status is CompetitionStatus.Running or CompetitionStatus.Finished && now < deadline.AcceptanceEndsAt
                && round is not null && now >= round.StartsAt,
            round?.Number, round?.Id, round?.StartsAt, round?.EndsAt,
            defaults.RoundDurationSeconds, defaults.HardeningDurationSeconds, defaults.AwdOptions,
            overrides.AwdOverrides, overrides.AttackPoints != null, overrides.VictimDefensePoolPoints != null,
            overrides.ServiceHealthyPoints != null, overrides.CheckerIntervalSeconds != null, overrides.AttackRewardMode != null,
            issued.Select(rule => new AwdPublicRulePolicy(rule.Id, rule.Revision, rule.EffectiveAt,
                rule.AttackRewardMode, rule.AttackPoints, rule.VictimDefensePoolPoints, rule.ServiceHealthyPoints,
                rule.CheckerIntervalSeconds, rule.Options, rule.Stores.Select(store =>
                    new AwdPublicStoreRule(store.StoreId, store.Name, store.Weight, store.FlagsPerIssue)).ToArray(),
                latest?.Policy ?? AwdScoringPolicy.From(rule), latest?.EventId)).ToArray(),
            new(null, null, null, resolved.AttackMode, resolved.AttackPoints, resolved.DefensePoints,
                resolved.SlaPoints, resolved.CheckerIntervalSeconds, resolved.Options, nextStores, AwdScoringPolicy.From(nextSnapshot), null));
    }
}
