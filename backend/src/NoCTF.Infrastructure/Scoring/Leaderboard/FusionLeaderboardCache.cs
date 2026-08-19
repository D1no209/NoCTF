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

    public Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken ct) =>
        cache.GetOrDefaultAsync<LeaderboardResponse?>(SnapshotKey(competitionId), null, token: ct).AsTask();

    public async Task<LeaderboardResponse?> GetFrozenAsync(Guid competitionId, CancellationToken ct)
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
        CancellationToken ct) =>
        await CreateCoreAsync(competitionId, null, projectedAt, ct);

    public async Task<LeaderboardResponse?> CreateWithConfigurationAsync(
        Guid competitionId,
        string competitionConfigurationJson,
        DateTimeOffset projectedAt,
        CancellationToken ct) =>
        await CreateCoreAsync(competitionId, competitionConfigurationJson, projectedAt, ct);

    private async Task<LeaderboardResponse?> CreateCoreAsync(
        Guid competitionId,
        string? competitionConfigurationJson,
        DateTimeOffset projectedAt,
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
                instance.RulesJson))
            .ToList();

        var users = await db.Users.AsNoTracking()
            .ToDictionaryAsync(user => user.Id, user => user.UserName, ct);
        var hintCosts = challengeEntities
            .SelectMany(challenge => challenge.Hints)
            .ToDictionary(hint => hint.Id, hint => hint.Cost);
        var facts = (await db.GameplayFacts.AsNoTracking()
                .Where(fact => fact.CompetitionId == competitionId)
                .OrderBy(fact => fact.OccurredAt)
                .ThenBy(fact => fact.Id)
                .ToListAsync(ct))
            .Select(fact => new LeaderboardGameplayFact(
                fact.Id,
                fact.TeamId,
                fact.CompetitionChallengeId,
                fact.Kind,
                fact.OccurredAt,
                fact.State,
                fact.Result,
                fact.FailureCode,
                fact.ReferenceKind,
                fact.ReferenceId,
                fact.VictimTeamId,
                fact.ActorUserId is Guid actorId ? users.GetValueOrDefault(actorId) : null,
                fact.Value,
                fact.ReferenceKind == GameplayFactReferenceKind.Hint && fact.ReferenceId is Guid hintId
                    ? hintCosts.GetValueOrDefault(hintId)
                    : null))
            .ToList();

        var lifecycleEvents = await db.CompetitionEvents.AsNoTracking()
            .Where(@event => @event.CompetitionId == competitionId
                && @event.Kind == CompetitionEventKind.CompetitionLifecycleChanged)
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

        var projection = projectionEngine.Project(new(
            competitionId,
            competition.Mode,
            teamFacts,
            facts,
            challenges,
            competitionConfigurationJson ?? competition.ConfigurationJson,
            competition.StartAt,
            lifecycle,
            awdRounds,
            projectedAt,
            competition.Status));
        return new LeaderboardResponse(competitionId, projectedAt, projection.Entries)
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
    }

    public async Task RefreshAsync(Guid competitionId, CancellationToken ct)
    {
        if (db.Database.IsInMemory())
        {
            var developmentResponse = await CreateAsync(
                competitionId, DateTimeOffset.UtcNow, ct);
            if (developmentResponse is null)
                return;
            await cache.SetAsync(SnapshotKey(competitionId), developmentResponse, token: ct);
            await cache.RemoveAsync(FailureKey(competitionId), token: ct);
            await publisher.PublishAsync(competitionId, developmentResponse.GeneratedAt, ct);
            return;
        }
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({competitionId.ToString("N")}, 0))",
                ct);
            var response = await CreateAsync(competitionId, DateTimeOffset.UtcNow, ct);
            if (response is null)
                return;
            await cache.SetAsync(SnapshotKey(competitionId), response, token: ct);
            await cache.RemoveAsync(FailureKey(competitionId), token: ct);
            await transaction.CommitAsync(ct);
            await publisher.PublishAsync(competitionId, response.GeneratedAt, ct);
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

    private static string SnapshotKey(Guid competitionId) => $"leaderboard:{competitionId:N}";
    private static string FailureKey(Guid competitionId) => $"leaderboard:{competitionId:N}:last-failure";
}
