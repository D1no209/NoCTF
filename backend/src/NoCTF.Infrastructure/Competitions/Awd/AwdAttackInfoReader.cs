using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

public sealed class AwdAttackInfoReader(NoCtfDbContext db) : IAwdAttackInfoReader
{
    public async Task<AwdAttackInfoPage?> ReadAsync(Guid competitionId, Guid challengeId, Guid ownTeamId,
        Guid targetTeamId, DateTimeOffset now, int offset, int limit, CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, IsolationLevel.RepeatableRead, ct);
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == competitionId
            && item.Mode == GameMode.Awd && item.DeletedAt == null, ct);
        if (competition?.ModeConfiguration is not AwdCompetitionModeConfiguration configuration
            || competition.Status is not (CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)) return null;
        if (!await db.CompetitionChallenges.AnyAsync(item => item.Id == challengeId && item.CompetitionId == competitionId
            && item.IsPublished && item.DeletedAt == null && db.Challenges.Any(template => template.Id == item.ChallengeId
                && template.DeletedAt == null), ct)) return null;
        var teams = db.Teams.Where(team => team.CompetitionId == competitionId && team.DeletedAt == null
            && !team.IsBanned && team.RegistrationStatus == TeamRegistrationStatus.Approved);
        if (!await teams.AnyAsync(team => team.Id == ownTeamId, ct)
            || !await teams.AnyAsync(team => team.Id == targetTeamId, ct)) return null;
        var deadline = await AwdCompetitionDeadline.ReadAsync(db, competitionId, competition.StartAt,
            competition.EndAt, configuration, now, ct);
        var position = now < deadline.EndsAt ? now : deadline.EndsAt;
        var runningTicks = deadline.Clock.ElapsedAt(position).Ticks;
        var hardeningTicks = deadline.Rounds.FirstOrDefault()?.ActiveStartTicks
            ?? TimeSpan.FromSeconds(configuration.HardeningDurationSeconds).Ticks;
        if (runningTicks < hardeningTicks && targetTeamId != ownTeamId) return null;
        var round = deadline.Rounds.LastOrDefault(item => item.StartsAt <= position);
        // A paused clock reports a provisional wall deadline at its closed interval.
        // Locator validity continues on the frozen active clock until resumption.
        if (round is null || competition.Status != CompetitionStatus.Paused && now >= deadline.AcceptanceEndsAt)
            return new([], 0);
        var gaps = deadline.Rounds.Where(item => item.Number < round.Number).Sum(item => (long)item.GapSeconds) * TimeSpan.TicksPerSecond;
        var activeTicks = Math.Min(runningTicks - gaps, round.ActiveEndTicks);
        // Select only explicit public metadata. Protected Flag/read-context columns never enter this projection.
        var query = db.AwdFlagIssues.AsNoTracking().Where(issue => issue.CompetitionId == competitionId
            && issue.CompetitionChallengeId == challengeId && issue.TeamId == targetTeamId
            && issue.State == AwdFlagIssueState.Issued && issue.IssuedAt <= now
            && issue.RoundNumber <= round.Number && issue.ActiveStartTicks <= activeTicks
            && (issue.ExpiryRound == null || round.Number < issue.ExpiryRound)
            && (issue.ActiveExpiryTicks == null || activeTicks < issue.ActiveExpiryTicks)
            && issue.PublicLocator != null && issue.PublicLocator != ""
            && db.ChallengeFlags.Any(flag => flag.Id == issue.FlagId && flag.DeletedAt == null))
            .Join(db.Set<AwdRoundStoreBudget>(), issue => new { issue.RulesId, issue.StoreId },
                store => new { store.RulesId, store.StoreId }, (issue, store) => new
                {
                    issue.FlagId, issue.RoundNumber, issue.StoreId, StoreName = store.Name, issue.Ordinal,
                    issue.PublicLocator, issue.IssuedAt, issue.ExpiryRound, issue.ActiveExpiryTicks
                });
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(item => item.RoundNumber).ThenBy(item => item.StoreName)
            .ThenBy(item => item.StoreId).ThenBy(item => item.Ordinal).ThenBy(item => item.FlagId)
            .Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 200)).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        return new(rows.Select(item => new AwdAttackInfoItem(item.FlagId, item.RoundNumber, item.StoreId,
            item.StoreName, item.Ordinal, item.PublicLocator!, item.IssuedAt, item.ExpiryRound,
            item.ActiveExpiryTicks is long expiry ? (double)(expiry - activeTicks) / TimeSpan.TicksPerSecond : null)).ToArray(), total);
    }
}
