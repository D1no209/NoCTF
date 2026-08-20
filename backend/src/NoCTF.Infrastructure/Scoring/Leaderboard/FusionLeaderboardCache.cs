using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class FusionLeaderboardCache(
    NoCtfDbContext db,
    ILeaderboardProjectionEngine projectionEngine,
    ILeaderboardRefreshPublisher publisher,
    IFusionCacheProvider caches) : ILeaderboardCache, ILeaderboardSnapshotFactory
{
    private sealed record LifecyclePayload(
        int SchemaVersion,
        CompetitionStatus From,
        CompetitionStatus To,
        bool Automatic,
        string? Reason);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.Leaderboards);

    public async Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken ct) =>
        (await cache.GetOrDefaultAsync<LeaderboardProjectionBundle?>(
            ProjectionKey(competitionId), null, token: ct))?.Legacy;

    public async Task<ScoreboardProjection?> GetScoreboardAsync(Guid competitionId, CancellationToken ct) =>
        (await cache.GetOrDefaultAsync<LeaderboardProjectionBundle?>(
            ProjectionKey(competitionId), null, token: ct))?.Scoreboard;

    public async Task<LeaderboardResponse?> GetFrozenAsync(Guid competitionId, CancellationToken ct)
    {
        var payload = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => competition.FrozenLeaderboardSnapshotJson)
            .SingleOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(payload)
            ? null
            : JsonSerializer.Deserialize<LeaderboardProjectionBundle>(payload, JsonOptions)?.Legacy;
    }

    public async Task<ScoreboardProjection?> GetFrozenScoreboardAsync(Guid competitionId, CancellationToken ct)
    {
        var payload = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => competition.FrozenLeaderboardSnapshotJson)
            .SingleOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(payload)
            ? null
            : JsonSerializer.Deserialize<LeaderboardProjectionBundle>(payload, JsonOptions)?.Scoreboard;
    }

    public async Task<LeaderboardResponse?> CreateAsync(
        Guid competitionId,
        DateTimeOffset projectedAt,
        CancellationToken ct) =>
        (await ProjectBundleAsync(competitionId, null, projectedAt, null, ct))?.Legacy;

    public Task<LeaderboardProjectionBundle?> CreateBundleAsync(
        Guid competitionId,
        DateTimeOffset projectedAt,
        CancellationToken ct) =>
        ProjectBundleAsync(competitionId, null, projectedAt, null, ct);

    public async Task<ScoreboardProjection?> CreateScoreboardAsync(
        Guid competitionId,
        DateTimeOffset projectedAt,
        CancellationToken ct) =>
        (await ProjectBundleAsync(competitionId, null, projectedAt, null, ct))?.Scoreboard;

    public async Task<ScoreboardProjection?> CreateScoreboardWindowAsync(
        Guid competitionId,
        int endingRound,
        DateTimeOffset projectedAt,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            ct);
        var scoreboard = (await ProjectBundleAsync(
            competitionId,
            null,
            projectedAt,
            endingRound,
            ct))?.Scoreboard;
        await transaction.CommitAsync(ct);
        return scoreboard;
    }

    public async Task<LeaderboardResponse?> CreateWithConfigurationAsync(
        Guid competitionId,
        string competitionConfigurationJson,
        DateTimeOffset projectedAt,
        CancellationToken ct) =>
        (await ProjectBundleAsync(
            competitionId,
            competitionConfigurationJson,
            projectedAt,
            null,
            ct))?.Legacy;

    private async Task<LeaderboardProjectionBundle?> ProjectBundleAsync(
        Guid competitionId,
        string? competitionConfigurationJson,
        DateTimeOffset projectedAt,
        int? scoreboardRoundWindowEnd,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == competitionId, ct);
        if (competition is null)
            return null;

        var trackConfiguration = CompetitionTrackConfiguration.ParseOrDefault(
            competition.Mode,
            competition.TrackConfigurationJson);
        var trackDefinitions = trackConfiguration.Tracks.ToDictionary(
            track => track.Key,
            StringComparer.OrdinalIgnoreCase);

        var teams = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.RegistrationStatus == TeamRegistrationStatus.Approved)
            .Select(team => new
            {
                team.Id,
                team.Name,
                team.TrackKey,
                team.IsBanned,
                team.RegisteredAt
            })
            .ToListAsync(ct);
        var teamFacts = teams.Select(team =>
        {
            var track = trackDefinitions.GetValueOrDefault(team.TrackKey)
                ?? trackConfiguration.DefaultTrack;
            return new LeaderboardTeamFact(
                team.Id,
                team.Name,
                team.IsBanned,
                false,
                team.RegisteredAt,
                track.Key,
                track.EarnsScore,
                track.EarnsBlood,
                track.AffectsDynamicChallengeScore,
                track.VisibleOnLeaderboard,
                track.AffectsCompetitiveResults);
        }).ToList();

        var challengeEntities = await db.CompetitionChallenges.AsNoTracking()
            .Where(instance => instance.CompetitionId == competitionId)
            .OrderBy(instance => instance.Order)
            .ThenBy(instance => instance.Id)
            .ToListAsync(ct);
        var templateIds = challengeEntities.Select(instance => instance.ChallengeId).ToArray();
        var templates = await db.Challenges.AsNoTracking()
            .Where(template => templateIds.Contains(template.Id))
            .ToDictionaryAsync(template => template.Id, ct);
        var challenges = challengeEntities
            .Where(instance => templates.ContainsKey(instance.ChallengeId))
            .Select(instance => new LeaderboardChallengeFact(
                instance.Id,
                templates[instance.ChallengeId].Direction,
                instance.CustomTitle ?? templates[instance.ChallengeId].Title,
                false,
                instance.RulesJson,
                instance.Order,
                instance.IsPublished,
                instance.Revision))
            .ToList();

        var hintCosts = challengeEntities
            .SelectMany(challenge => challenge.Hints)
            .ToDictionary(hint => hint.Id, hint => hint.Cost);
        var lifecycleEvents = await db.CompetitionEvents.AsNoTracking()
            .Where(@event => @event.CompetitionId == competitionId
                && @event.Kind == CompetitionEventKind.CompetitionLifecycleChanged
                && @event.OccurredAt <= projectedAt)
            .OrderBy(@event => @event.OccurredAt)
            .ThenBy(@event => @event.Id)
            .ToListAsync(ct);
        var lifecycle = lifecycleEvents.Select(@event =>
        {
            var payload = JsonSerializer.Deserialize<LifecyclePayload>(@event.PayloadJson, JsonOptions)!;
            return new CompetitionLifecycleTransition
            {
                Id = @event.Id,
                CompetitionId = @event.CompetitionId,
                From = payload.From,
                To = payload.To,
                ActorId = @event.ActorUserId,
                Reason = payload.Reason,
                Automatic = payload.Automatic,
                OccurredAt = @event.OccurredAt
            };
        }).ToList();
        var competitionStatusAtProjection = lifecycle.Count > 0
            ? lifecycle[^1].To
            : competition.Status;
        var factRows = await LeaderboardFactProjectionReader.ReadAsync(
            db,
            competitionId,
            competition.Mode,
            competitionConfigurationJson ?? competition.ConfigurationJson,
            competition.StartAt,
            lifecycle,
            projectedAt,
            hintCosts,
            teamFacts,
            scoreboardRoundWindowEnd,
            ct);
        var actorIds = factRows.Legacy
            .Concat(factRows.Scoreboard)
            .Where(fact => fact.ActorUserId is not null)
            .Select(fact => fact.ActorUserId!.Value)
            .Distinct()
            .ToArray();
        var users = await db.Users.AsNoTracking()
            .Where(user => actorIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName, ct);
        LeaderboardGameplayFact[] EnrichActors(IEnumerable<LeaderboardGameplayFact> facts) => facts
            .Select(fact => fact with
            {
                SubmitterName = fact.ActorUserId is Guid actorId
                    ? users.GetValueOrDefault(actorId)
                    : null
            })
            .ToArray();
        var legacyFacts = EnrichActors(factRows.Legacy);
        var scoreboardFacts = EnrichActors(factRows.Scoreboard);

        IReadOnlyList<LeaderboardAwdRoundFact> awdRounds = [];
        if (competition.Mode == GameMode.Awd)
        {
            var competitionChallengeIds = challengeEntities.Select(challenge => challenge.Id).ToArray();
            awdRounds = await db.ChallengeFlags.AsNoTracking()
                .Where(flag => flag.TeamId != null
                    && flag.CompetitionChallengeId != null
                    && competitionChallengeIds.Contains(flag.CompetitionChallengeId.Value)
                    && flag.SpecificationKind == SpecificationKind.AwdRound
                    && flag.SpecificationId != null
                    && flag.ValidStart != null
                    && flag.ValidUntil != null)
                .Select(flag => new LeaderboardAwdRoundFact(
                    flag.CompetitionChallengeId!.Value,
                    flag.TeamId!.Value,
                    flag.SpecificationId!.Value,
                    flag.ValidStart!.Value,
                    flag.ValidUntil!.Value))
                .ToListAsync(ct);
        }

        var projectionInput = new LeaderboardProjectionInput(
            competitionId,
            competition.Mode,
            teamFacts,
            legacyFacts,
            challenges,
            competitionConfigurationJson ?? competition.ConfigurationJson,
            competition.StartAt,
            lifecycle,
            awdRounds,
            projectedAt,
            competitionStatusAtProjection,
            scoreboardFacts,
            scoreboardRoundWindowEnd);
        var projection = projectionEngine.Project(projectionInput);
        var legacy = new LeaderboardResponse(competitionId, projectedAt, projection.Entries)
        {
            Challenges = projection.Challenges,
            Tracks = trackConfiguration.Tracks.Select(track => new LeaderboardTrackInfo(
                track.Key,
                track.Name,
                track.IsInternal,
                track.VisibleOnLeaderboard)).ToArray(),
            Visibility = CompetitionLeaderboardVisibility.Normal,
            DataScope = LeaderboardDataScope.Live,
            DataAsOf = projectedAt,
            CurrentRound = projection.CurrentRound,
            SettledThroughRound = projection.SettledThroughRound,
            RoundDurationSeconds = projection.RoundDurationSeconds,
            CurrentRoundRemainingSeconds = projection.CurrentRoundRemainingSeconds
        };
        var scoreboard = projectionEngine.ProjectScoreboard(projectionInput);
        scoreboard = scoreboard with
        {
            Snapshot = scoreboard.Snapshot with
            {
                Tracks = trackConfiguration.Tracks.Select(track => new ScoreboardTrack(
                    track.Key,
                    track.Name,
                    track.IsInternal,
                    track.VisibleOnLeaderboard)).ToArray(),
                Visibility = CompetitionLeaderboardVisibility.Normal,
                DataScope = LeaderboardDataScope.Live,
                DataAsOf = projectedAt
            }
        };
        return new(legacy, scoreboard);
    }

    public async Task RefreshAsync(Guid competitionId, CancellationToken ct)
    {
        if (db.Database.IsInMemory())
        {
            var developmentResponse = await ProjectBundleAsync(
                competitionId, null, DateTimeOffset.UtcNow, null, ct);
            if (developmentResponse is null)
                return;
            var previous = await cache.GetOrDefaultAsync<LeaderboardProjectionBundle?>(
                ProjectionKey(competitionId), null, token: ct);
            developmentResponse = AdvanceScoreboardVersion(developmentResponse, previous);
            await cache.SetAsync(ProjectionKey(competitionId), developmentResponse, token: ct);
            await cache.RemoveAsync(FailureKey(competitionId), token: ct);
            await publisher.PublishAsync(developmentResponse.Scoreboard, ct);
            return;
        }
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({competitionId.ToString("N")}, 0))",
                ct);
            var response = await ProjectBundleAsync(
                competitionId,
                null,
                DateTimeOffset.UtcNow,
                null,
                ct);
            if (response is null)
                return;
            var previous = await cache.GetOrDefaultAsync<LeaderboardProjectionBundle?>(
                ProjectionKey(competitionId), null, token: ct);
            response = AdvanceScoreboardVersion(response, previous);
            await cache.SetAsync(ProjectionKey(competitionId), response, token: ct);
            await cache.RemoveAsync(FailureKey(competitionId), token: ct);
            await transaction.CommitAsync(ct);
            await publisher.PublishAsync(response.Scoreboard, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            db.ChangeTracker.Clear();
            await db.Competitions
                .Where(competition => competition.Id == competitionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(competition => competition.LeaderboardDirty, true), CancellationToken.None);
            await cache.SetAsync(FailureKey(competitionId), DateTimeOffset.UtcNow, token: CancellationToken.None);
            throw;
        }
    }

    public async Task InvalidateAsync(Guid competitionId, CancellationToken ct)
    {
        if (db.Database.IsInMemory())
        {
            var competition = await db.Competitions.SingleOrDefaultAsync(
                candidate => candidate.Id == competitionId, ct);
            if (competition is not null)
            {
                competition.LeaderboardDirty = true;
                await db.SaveChangesAsync(ct);
            }
            return;
        }
        _ = await db.Competitions
            .Where(competition => competition.Id == competitionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(competition => competition.LeaderboardDirty, true), ct);
    }

    public async Task<LeaderboardCacheStatus> GetStatusAsync(Guid competitionId, CancellationToken ct)
    {
        var failure = await cache.GetOrDefaultAsync<DateTimeOffset?>(
            FailureKey(competitionId), null, token: ct);
        return new(failure);
    }

    private static string ProjectionKey(Guid competitionId) => $"projection:v2:{competitionId:N}";
    private static string FailureKey(Guid competitionId) => $"leaderboard:{competitionId:N}:last-failure";

    private static LeaderboardProjectionBundle AdvanceScoreboardVersion(
        LeaderboardProjectionBundle candidate,
        LeaderboardProjectionBundle? previous)
    {
        if (previous is null || candidate.Scoreboard.Snapshot.Version > previous.Scoreboard.Snapshot.Version)
            return candidate;
        var nextVersion = previous.Scoreboard.Snapshot.Version == long.MaxValue
            ? long.MaxValue
            : previous.Scoreboard.Snapshot.Version + 1;
        return candidate with
        {
            Scoreboard = candidate.Scoreboard with
            {
                Snapshot = candidate.Scoreboard.Snapshot with { Version = nextVersion }
            }
        };
    }
}
