using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Persistence;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class FusionLeaderboardCache(
    NoCtfDbContext db,
    ILeaderboardProjectionEngine projectionEngine,
    IConfiguration configuration,
    ILeaderboardRefreshPublisher publisher,
    IFusionCache cache) : ILeaderboardCache, ILeaderboardSnapshotFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeSpan ttl = TimeSpan.FromSeconds(Math.Max(5, configuration.GetValue("Leaderboard:CacheTtlSeconds", 60)));

    public async Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken ct)
    {
        var targetRevision = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => (long?)competition.LeaderboardRevision)
            .SingleOrDefaultAsync(ct);
        if (targetRevision is null)
            return null;
        var snapshot = await cache.GetOrDefaultAsync<LeaderboardResponse?>(
            SnapshotKey(competitionId, targetRevision.Value),
            null,
            token: ct);
        if (snapshot is null)
            return null;
        return snapshot with
        {
            TargetRevision = targetRevision.Value,
            Stale = false,
            LastFailureAt = null
        };
    }

    public async Task<LeaderboardResponse?> GetFrozenAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var payload = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => competition.FrozenLeaderboardSnapshotJson)
            .SingleOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(payload)
            ? null
            : JsonSerializer.Deserialize<LeaderboardResponse>(payload, JsonOptions);
    }

    public async Task<LeaderboardResponse?> CreateAsync(
        Guid competitionId,
        DateTimeOffset projectedAt,
        bool historical,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == competitionId, ct);
        if (competition is null)
            return null;

        var teamsQuery = historical
            ? db.Teams.IgnoreQueryFilters().AsNoTracking()
                .Where(team => team.CompetitionId == competitionId
                    && team.RegisteredAt <= projectedAt
                    && (team.DeletedAt == null || team.DeletedAt > projectedAt))
            : db.Teams.AsNoTracking()
                .Where(team => team.CompetitionId == competitionId);
        var teams = await teamsQuery
            .Where(team => team.RegistrationStatus == TeamRegistrationStatus.Approved)
            .Select(team => new LeaderboardTeamFact(
                team.Id,
                team.Name,
                historical
                    ? team.IsBanned && (team.BannedAt == null || team.BannedAt <= projectedAt)
                    : team.IsBanned,
                false,
                team.RegisteredAt))
            .ToListAsync(ct);

        var challengeInstances = historical
            ? db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
                .Where(instance => instance.CompetitionId == competitionId
                    && (instance.DeletedAt == null || instance.DeletedAt > projectedAt))
            : db.CompetitionChallenges.AsNoTracking()
                .Where(instance => instance.CompetitionId == competitionId);
        var challengeTemplates = historical
            ? db.Challenges.IgnoreQueryFilters().AsNoTracking()
                .Where(template => template.DeletedAt == null || template.DeletedAt > projectedAt)
            : db.Challenges.AsNoTracking();
        var challenges = await challengeInstances
            .Join(
                challengeTemplates,
                instance => instance.ChallengeId,
                template => template.Id,
                (instance, template) => new { Instance = instance, Template = template })
            .OrderBy(item => item.Instance.Order)
            .ThenBy(item => item.Instance.Id)
            .Select(item => new LeaderboardChallengeFact(
                item.Instance.Id,
                item.Template.Direction,
                item.Template.Title,
                false,
                item.Instance.RulesJson))
            .ToListAsync(ct);

        IReadOnlyList<LeaderboardSubmissionFact> submissions;
        if (historical)
        {
            var historicalSubmissions = db.Submissions.AsNoTracking()
                .Where(submission => submission.CompetitionId == competitionId
                    && submission.ReceivedAt <= projectedAt)
                .Select(submission => new
                {
                    Submission = submission,
                    ScoringEventId = db.ScoringEvents.IgnoreQueryFilters()
                        .Where(scoringEvent => scoringEvent.CompetitionId == competitionId
                            && scoringEvent.SubmissionId == submission.Id
                            && scoringEvent.CreatedAt <= projectedAt)
                        .OrderByDescending(scoringEvent => scoringEvent.ProcessingVersion)
                        .ThenByDescending(scoringEvent => scoringEvent.CreatedAt)
                        .ThenByDescending(scoringEvent => scoringEvent.Id)
                        .Select(scoringEvent => (Guid?)scoringEvent.Id)
                        .FirstOrDefault()
                })
                .Where(item => item.ScoringEventId != null);
            submissions = await historicalSubmissions
                .Join(
                    db.ScoringEvents.IgnoreQueryFilters().AsNoTracking(),
                    item => item.ScoringEventId,
                    scoringEvent => (Guid?)scoringEvent.Id,
                    (item, scoringEvent) => new { item.Submission, ScoringEvent = scoringEvent })
                .Join(
                    db.Users.AsNoTracking(),
                    item => item.Submission.SubmittedByUserId,
                    user => user.Id,
                    (item, user) => new LeaderboardSubmissionFact(
                        item.Submission.Id,
                        item.Submission.TeamId,
                        item.Submission.CompetitionChallengeId,
                        item.Submission.Kind,
                        item.Submission.ReceivedAt,
                        item.ScoringEvent,
                        item.ScoringEvent.VictimTeamId,
                        user.UserName))
                .ToListAsync(ct);
        }
        else
        {
            submissions = await db.Submissions.AsNoTracking()
                .Where(submission => submission.CompetitionId == competitionId
                    && submission.CurrentScoringEventId != null)
                .Join(
                    db.ScoringEvents.AsNoTracking(),
                    submission => submission.CurrentScoringEventId,
                    scoringEvent => (Guid?)scoringEvent.Id,
                    (submission, scoringEvent) => new { Submission = submission, ScoringEvent = scoringEvent })
                .Join(
                    db.Users.AsNoTracking(),
                    item => item.Submission.SubmittedByUserId,
                    user => user.Id,
                    (item, user) => new LeaderboardSubmissionFact(
                        item.Submission.Id,
                        item.Submission.TeamId,
                        item.Submission.CompetitionChallengeId,
                        item.Submission.Kind,
                        item.Submission.ReceivedAt,
                        item.ScoringEvent,
                        item.ScoringEvent.VictimTeamId,
                        user.UserName))
                .ToListAsync(ct);
        }

        var systemEvents = historical
            ? db.ScoringEvents.IgnoreQueryFilters().AsNoTracking()
                .Where(scoringEvent => scoringEvent.CompetitionId == competitionId
                    && scoringEvent.SubmissionId == null
                    && scoringEvent.CreatedAt <= projectedAt
                    && scoringEvent.OccurredAt <= projectedAt
                    && (scoringEvent.DeletedAt == null || scoringEvent.DeletedAt > projectedAt))
            : db.ScoringEvents.AsNoTracking()
                .Where(scoringEvent => scoringEvent.CompetitionId == competitionId
                    && scoringEvent.SubmissionId == null);
        var system = await systemEvents
            .Select(scoringEvent => new LeaderboardSystemFact(
                scoringEvent,
                scoringEvent.Kind == ScoringEventKind.HintUnlock
                    && scoringEvent.SpecificationId != null
                    ? db.Set<CompetitionChallengeHint>()
                        .Where(hint => hint.Id == scoringEvent.SpecificationId
                            && (hint.DeletedAt == null
                                || historical && hint.DeletedAt > projectedAt))
                        .Select(hint => hint.Cost)
                        .SingleOrDefault()
                    : 0))
            .ToListAsync(ct);
        var lifecycleAudits = await db.Set<CompetitionLifecycleAudit>().AsNoTracking()
            .Where(audit => audit.CompetitionId == competitionId
                && (!historical || audit.OccurredAt <= projectedAt))
            .ToListAsync(ct);

        IReadOnlyList<LeaderboardAwdRoundFact> awdRounds = [];
        if (competition.Mode == GameMode.Awd)
        {
            var flags = historical
                ? db.ChallengeFlags.IgnoreQueryFilters().AsNoTracking()
                    .Where(flag => flag.DeletedAt == null || flag.DeletedAt > projectedAt)
                : db.ChallengeFlags.AsNoTracking();
            awdRounds = await flags
                .Where(flag => flag.TeamId != null
                    && flag.CompetitionChallengeId != null
                    && flag.SpecificationKind == SpecificationKind.AwdRound
                    && flag.SpecificationId != null
                    && flag.ValidStart != null
                    && flag.ValidUntil != null)
                .Join(
                    challengeInstances,
                    flag => flag.CompetitionChallengeId,
                    challenge => (Guid?)challenge.Id,
                    (flag, _) => new LeaderboardAwdRoundFact(
                        flag.CompetitionChallengeId!.Value,
                        flag.TeamId!.Value,
                        flag.SpecificationId!.Value,
                        flag.ValidStart!.Value,
                        flag.ValidUntil!.Value))
                .ToListAsync(ct);
        }

        var projection = projectionEngine.Project(new(
            competitionId,
            competition.Mode,
            teams,
            submissions,
            system,
            challenges,
            competition.ConfigurationJson,
            competition.StartAt,
            lifecycleAudits,
            awdRounds,
            projectedAt));
        return new LeaderboardResponse(competitionId, projectedAt, projection.Entries)
        {
            Subjects = projection.Subjects,
            Bloods = projection.Bloods,
            Series = projection.Series,
            Challenges = projection.Challenges,
            SnapshotRevision = competition.LeaderboardRevision,
            TargetRevision = competition.LeaderboardRevision,
            Stale = false,
            Visibility = CompetitionLeaderboardVisibility.Normal,
            DataScope = LeaderboardDataScope.Live,
            DataAsOf = projectedAt
        };
    }

    public async Task RefreshAsync(Guid competitionId, CancellationToken ct)
    {
        var attemptedRevision = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => (long?)competition.LeaderboardRevision)
            .SingleOrDefaultAsync(ct);
        if (attemptedRevision is null)
            return;
        try
        {
            var response = await cache.GetOrSetAsync<LeaderboardResponse?>(
                SnapshotKey(competitionId, attemptedRevision.Value),
                async (_, token) =>
                {
                    var projected = await CreateAsync(
                        competitionId,
                        DateTimeOffset.UtcNow,
                        historical: false,
                        token);
                    if (projected is not null)
                        await publisher.PublishAsync(
                            competitionId,
                            projected.GeneratedAt,
                            token);
                    return projected;
                },
                options => options.SetDuration(ttl),
                token: ct);
            if (response is null)
                return;
            await cache.RemoveAsync(
                FailureKey(competitionId, response.SnapshotRevision),
                token: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await cache.SetAsync(
                FailureKey(competitionId, attemptedRevision.Value),
                DateTimeOffset.UtcNow,
                options => options.SetDuration(ttl),
                token: CancellationToken.None);
            throw;
        }
    }

    public Task InvalidateAsync(Guid competitionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task<LeaderboardCacheStatus> GetStatusAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var targetRevision = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => competition.LeaderboardRevision)
            .SingleOrDefaultAsync(ct);
        var failure = await cache.GetOrDefaultAsync<DateTimeOffset?>(
            FailureKey(competitionId, targetRevision),
            null,
            token: ct);
        return new(targetRevision, failure);
    }

    private static string SnapshotKey(Guid competitionId, long revision) =>
        $"leaderboard:v3:{competitionId:N}:snapshot:{revision}";

    private static string FailureKey(Guid competitionId, long revision) =>
        $"leaderboard:v3:{competitionId:N}:failure:{revision}";
}
