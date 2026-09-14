using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Application.Observability;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class FusionLeaderboardCache(
    NoCtfDbContext db,
    ILeaderboardProjectionEngine projectionEngine,
    ILeaderboardRefreshPublisher publisher,
    IFusionCacheProvider caches,
    ILeaderboardPublicationFence? publicationFence = null,
    LeaderboardProjectionKeyedLock? projectionKeyedLock = null,
    TimeProvider? clock = null) : ILeaderboardCache, ILeaderboardSnapshotFactory
{
    private sealed record LifecyclePayload(
        int SchemaVersion,
        CompetitionStatus From,
        CompetitionStatus To,
        bool Automatic,
        string? Reason);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.Leaderboards);
    private readonly LeaderboardProjectionKeyedLock keyedLock = projectionKeyedLock ?? new();
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private const int CurrentAwdpRoundProjectionFormat = 1;

    public async Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken ct) =>
        (await GetOrRebuildPublishedBundleAsync(competitionId, ct))?.Legacy;

    public async Task<ScoreboardProjection?> GetScoreboardAsync(Guid competitionId, CancellationToken ct) =>
        (await GetOrRebuildPublishedBundleAsync(competitionId, ct))?.Scoreboard;

    public async Task<LeaderboardResponse?> GetFrozenAsync(Guid competitionId, CancellationToken ct)
        => (await GetFrozenBundleAsync(competitionId, ct))?.Legacy;

    public async Task<ScoreboardProjection?> GetFrozenScoreboardAsync(Guid competitionId, CancellationToken ct)
        => (await GetFrozenBundleAsync(competitionId, ct))?.Scoreboard;

    private async Task<LeaderboardProjectionBundle?> GetFrozenBundleAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var frozenAt = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => competition.FrozenStartAt)
            .SingleOrDefaultAsync(ct);
        return frozenAt is null
            ? null
            : await ProjectBundleAsync(competitionId, null, frozenAt.Value, null, ct);
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
        var officialWindow = competition.Mode == GameMode.Ctf
            ? await CompetitionOfficialWindowReader.ReadAsync(
                db,
                competition.Id,
                competition.StartAt,
                competition.EndAt,
                ct)
            : CompetitionOfficialWindow.Resolve(
                competition.StartAt,
                competition.EndAt);

        var trackConfiguration = CompetitionTrackConfiguration.EffectiveFor(
            competition.Mode,
            competition.TracksEnabled,
            competition.TrackConfigurationJson);
        var trackDefinitions = trackConfiguration.Tracks.ToDictionary(
            track => track.Key,
            StringComparer.OrdinalIgnoreCase);

        var teams = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && (competition.Mode != GameMode.Ctf
                    || team.RegisteredAt < officialWindow.EndAt)
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
                templates[instance.ChallengeId].DefinitionJson,
                templates[instance.ChallengeId].Mode == GameMode.Ctf
                    ? NoCTF.GameModes.Ctf.Configuration.CtfConfigurationUpgrader
                        .ParseChallenge(templates[instance.ChallengeId].DefinitionJson)
                        .InteractionKind
                    : CtfInteractionKind.FlagSubmission))
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
        var awdWindow = competition.Mode == GameMode.Awd
            ? await ReadAwdScoreboardWindowAsync(
                challengeEntities.Select(challenge => challenge.Id).ToArray(),
                projectedAt,
                scoreboardRoundWindowEnd,
                ct)
            : AwdScoreboardWindow.Empty;
        var factRows = await LeaderboardFactProjectionReader.ReadAsync(
            db,
            competitionId,
            competition.Mode,
            competitionStatusAtProjection,
            competitionConfigurationJson ?? competition.ConfigurationJson,
            competition.StartAt,
            competition.EndAt,
            lifecycle,
            projectedAt,
            hintCosts,
            teamFacts,
            scoreboardRoundWindowEnd,
            awdWindow.Rounds,
            challenges,
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

        var selectedRoundWindowEnd = competition.Mode == GameMode.Awd
            ? awdWindow.EndRound
            : scoreboardRoundWindowEnd;
        var projectionInput = new LeaderboardProjectionInput(
            competitionId,
            competition.Mode,
            teamFacts,
            legacyFacts,
            challenges,
            competitionConfigurationJson ?? competition.ConfigurationJson,
            competition.StartAt,
            lifecycle,
            awdWindow.Rounds,
            projectedAt,
            competitionStatusAtProjection,
            scoreboardFacts,
            selectedRoundWindowEnd,
            awdWindow.LatestRound,
            factRows.AwdAggregates);
        var outputs = projectionEngine.ProjectOutputs(projectionInput);
        var projection = outputs.Legacy;
        var legacy = new LeaderboardResponse(competitionId, projectedAt, projection.Entries)
        {
            Challenges = projection.Challenges,
            Tracks = trackConfiguration.Tracks.Select(track => new LeaderboardTrackInfo(
                track.Key,
                track.Name,
                track.IsInternal,
                track.VisibleOnLeaderboard)).ToArray(),
            TracksEnabled = competition.TracksEnabled,
            Visibility = CompetitionLeaderboardVisibility.Normal,
            DataScope = LeaderboardDataScope.Live,
            DataAsOf = projectedAt,
            CurrentRound = projection.CurrentRound,
            SettledThroughRound = projection.SettledThroughRound,
            RoundDurationSeconds = projection.RoundDurationSeconds,
            CurrentRoundRemainingSeconds = projection.CurrentRoundRemainingSeconds
        };
        var scoreboard = outputs.Scoreboard;
        ScoreboardProjection AddResponseMetadata(ScoreboardProjection value) => value with
        {
            Snapshot = value.Snapshot with
            {
                Tracks = trackConfiguration.Tracks.Select(track => new ScoreboardTrack(
                    track.Key,
                    track.Name,
                    track.IsInternal,
                    track.VisibleOnLeaderboard)).ToArray(),
                TracksEnabled = competition.TracksEnabled,
                Visibility = CompetitionLeaderboardVisibility.Normal,
                DataScope = LeaderboardDataScope.Live,
                DataAsOf = projectedAt
            }
        };
        scoreboard = AddResponseMetadata(scoreboard);
        var publishedChallengeIds = challenges
            .Where(challenge => challenge.IsPublished)
            .Select(challenge => challenge.Id)
            .ToHashSet();
        bool IsParticipantVisible(LeaderboardGameplayFact fact) =>
            fact.CompetitionChallengeId is not Guid challengeId
            || publishedChallengeIds.Contains(challengeId);
        ScoreboardProjection participantScoreboard;
        var participantProjectionMatchesFull = publishedChallengeIds.Count == challenges.Count
            && legacyFacts.All(IsParticipantVisible)
            && scoreboardFacts.All(IsParticipantVisible)
            && awdWindow.Rounds.All(round =>
                publishedChallengeIds.Contains(round.CompetitionChallengeId))
            && (factRows.AwdAggregates is null || factRows.AwdAggregates.All(fact =>
                publishedChallengeIds.Contains(fact.CompetitionChallengeId)));
        if (participantProjectionMatchesFull)
        {
            participantScoreboard = scoreboard;
        }
        else
        {
            var participantInput = projectionInput with
            {
                Challenges = challenges.Where(challenge => challenge.IsPublished).ToArray(),
                GameplayFacts = legacyFacts.Where(IsParticipantVisible).ToArray(),
                ScoreboardGameplayFacts = scoreboardFacts.Where(IsParticipantVisible).ToArray(),
                AwdRounds = awdWindow.Rounds
                    .Where(round => publishedChallengeIds.Contains(round.CompetitionChallengeId))
                    .ToArray(),
                AwdAggregates = factRows.AwdAggregates?
                    .Where(fact => publishedChallengeIds.Contains(fact.CompetitionChallengeId))
                    .ToArray()
            };
            participantScoreboard = AddResponseMetadata(
                projectionEngine.ProjectOutputs(participantInput).Scoreboard);
        }
        scoreboard = scoreboard with
        {
            ParticipantView = ScoreboardAudienceView.From(participantScoreboard)
        };
        return new(legacy, scoreboard)
        {
            AwdpRoundProjectionFormat = competition.Mode == GameMode.Awdp ? CurrentAwdpRoundProjectionFormat : 0,
            ValidUntil = competition.Mode == GameMode.Awdp
                && competitionStatusAtProjection == CompetitionStatus.Running
                && projection.CurrentRoundRemainingSeconds is > 0
                ? projectedAt.AddSeconds(projection.CurrentRoundRemainingSeconds.Value)
                : null
        };
    }

    private async Task<AwdScoreboardWindow> ReadAwdScoreboardWindowAsync(
        IReadOnlyList<Guid> competitionChallengeIds,
        DateTimeOffset projectedAt,
        int? requestedEndingRound,
        CancellationToken ct)
    {
        if (competitionChallengeIds.Count == 0)
            return AwdScoreboardWindow.Empty;

        var roundMetadata = db.ChallengeFlags.AsNoTracking()
            .Where(flag => flag.DeletedAt == null
                && flag.TeamId != null
                && flag.CompetitionChallengeId != null
                && competitionChallengeIds.Contains(flag.CompetitionChallengeId.Value)
                && flag.SpecificationKind == SpecificationKind.AwdRound
                && flag.SpecificationId != null
                && flag.ValidStart != null
                && flag.ValidUntil != null
                && flag.ValidStart <= projectedAt)
            .GroupBy(flag => flag.SpecificationId!.Value)
            .Select(group => new
            {
                RoundId = group.Key,
                StartsAt = group.Min(flag => flag.ValidStart!.Value),
                EndsAt = group.Max(flag => flag.ValidUntil!.Value)
            })
            .OrderBy(round => round.StartsAt)
            .ThenBy(round => round.RoundId);
        var latestRound = await roundMetadata.CountAsync(ct);
        if (latestRound == 0)
            return AwdScoreboardWindow.Empty;

        var endRound = Math.Clamp(requestedEndingRound ?? latestRound, 1, latestRound);
        var startRound = Math.Max(1, endRound - ScoreboardRoundWindow.DefaultSize + 1);
        var selected = await roundMetadata
            .Skip(startRound - 1)
            .Take(endRound - startRound + 1)
            .ToListAsync(ct);
        var selectedRoundIds = selected.Select(round => round.RoundId).ToArray();
        var rounds = await db.ChallengeFlags.AsNoTracking()
            .Where(flag => flag.DeletedAt == null
                && flag.TeamId != null
                && flag.CompetitionChallengeId != null
                && competitionChallengeIds.Contains(flag.CompetitionChallengeId.Value)
                && flag.SpecificationKind == SpecificationKind.AwdRound
                && flag.SpecificationId != null
                && selectedRoundIds.Contains(flag.SpecificationId.Value)
                && flag.ValidStart != null
                && flag.ValidUntil != null)
            .Select(flag => new LeaderboardAwdRoundFact(
                flag.CompetitionChallengeId!.Value,
                flag.TeamId!.Value,
                flag.SpecificationId!.Value,
                flag.ValidStart!.Value,
                flag.ValidUntil!.Value))
            .ToListAsync(ct);
        return new(rounds, startRound, endRound, latestRound);
    }

    public async Task RefreshAsync(Guid competitionId, CancellationToken ct)
    {
        if (db.Database.IsInMemory())
        {
            var developmentResponse = await ProjectBundleAsync(
                competitionId, null, timeProvider.GetUtcNow(), null, ct);
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
        var closeConnection = db.Database.GetDbConnection().State != ConnectionState.Open;
        var projectionLockAcquired = false;
        var publicationPhase = false;
        LeaderboardProjectionBundle? response = null;
        long? publicationToken = null;
        try
        {
            if (closeConnection)
                await db.Database.OpenConnectionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_lock(hashtextextended({competitionId.ToString("N")}, 0))",
                ct);
            projectionLockAcquired = true;
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                ct);
            var projectionStarted = Stopwatch.GetTimestamp();
            try
            {
                response = await ProjectBundleAsync(
                    competitionId,
                    null,
                    timeProvider.GetUtcNow(),
                    null,
                    ct);
            }
            catch
            {
                NoCtfTelemetry.RecordLeaderboardProjection(
                    "unknown",
                    "failure",
                    Stopwatch.GetElapsedTime(projectionStarted).TotalSeconds,
                    0,
                    0);
                throw;
            }
            if (response is null)
                return;
            NoCtfTelemetry.RecordLeaderboardProjection(
                response.Scoreboard.Schema.Mode.ToString().ToLowerInvariant(),
                "success",
                Stopwatch.GetElapsedTime(projectionStarted).TotalSeconds,
                response.Scoreboard.EntryAllocations.Count,
                response.Scoreboard.Snapshot.Teams.Count);
            await transaction.CommitAsync(ct);
            publicationPhase = true;

            if (publicationFence is null)
            {
                var previous = await cache.GetOrDefaultAsync<LeaderboardProjectionBundle?>(
                    ProjectionKey(competitionId), null, token: ct);
                response = AdvanceScoreboardVersion(response, previous);
            }
            else
            {
                publicationToken = await publicationFence.IssueAsync(
                    competitionId,
                    response.Scoreboard.Snapshot.Version,
                    ct);
                response = SetScoreboardVersion(response, publicationToken.Value);
            }

            await ReleaseProjectionLockAsync(competitionId);
            projectionLockAcquired = false;
            if (closeConnection)
                await db.Database.CloseConnectionAsync();

            if (publicationFence is not null)
            {
                var payload = JsonSerializer.Serialize(response, JsonOptions);
                var accepted = await publicationFence.TryCommitAsync(
                    competitionId,
                    publicationToken!.Value,
                    payload,
                    ct);
                if (!accepted)
                    return;
            }

            await cache.SetAsync(ProjectionKey(competitionId), response, token: ct);
            if (publicationFence is not null
                && !await publicationFence.IsCurrentAsync(
                    competitionId,
                    publicationToken!.Value,
                    ct))
            {
                await RepairFusionCacheAsync(competitionId, ct);
                return;
            }

            await publisher.PublishAsync(response.Scoreboard, ct);
            await cache.RemoveAsync(FailureKey(competitionId), token: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            if (publicationPhase)
                NoCtfTelemetry.RecordLeaderboardPublishFailure("projection");
            await cache.SetAsync(FailureKey(competitionId), timeProvider.GetUtcNow(), token: CancellationToken.None);
            throw;
        }
        finally
        {
            if (projectionLockAcquired)
                await ReleaseProjectionLockAsync(competitionId);
            if (closeConnection && db.Database.GetDbConnection().State == ConnectionState.Open)
                await db.Database.CloseConnectionAsync();
        }
    }

    public async Task InvalidateAsync(Guid competitionId, CancellationToken ct)
    {
        var outcome = "success";
        try
        {
            if (publicationFence is not null)
            {
                await publicationFence.InvalidateAsync(
                    competitionId,
                    timeProvider.GetUtcNow().UtcTicks,
                    ct);
            }
            await cache.RemoveAsync(ProjectionKey(competitionId), token: ct);
        }
        catch
        {
            outcome = "failure";
            throw;
        }
        finally
        {
            NoCtfTelemetry.RecordLeaderboardInvalidation(outcome);
        }
    }

    public async Task<LeaderboardCacheStatus> GetStatusAsync(Guid competitionId, CancellationToken ct)
    {
        var failure = await cache.GetOrDefaultAsync<DateTimeOffset?>(
            FailureKey(competitionId), null, token: ct);
        return new(failure);
    }

    private static string ProjectionKey(Guid competitionId) => $"projection:v2:{competitionId:N}";
    private static string FailureKey(Guid competitionId) => $"leaderboard:{competitionId:N}:last-failure";

    private async Task<LeaderboardProjectionBundle?> GetOrRebuildPublishedBundleAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var published = await GetPublishedBundleAsync(competitionId, ct);
        if (published is not null)
            return published;

        using var projectionLock = await keyedLock.EnterAsync(competitionId, ct);
        published = await GetPublishedBundleAsync(competitionId, ct);
        if (published is not null)
            return published;

        try
        {
            await RefreshAsync(competitionId, ct);
            published = await GetPublishedBundleAsync(competitionId, ct);
            NoCtfTelemetry.RecordLeaderboardCacheMissRebuild(
                published is null ? "not_found" : "success");
            return published;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            NoCtfTelemetry.RecordLeaderboardCacheMissRebuild("failure");
            return null;
        }
    }

    private async Task<LeaderboardProjectionBundle?> GetPublishedBundleAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        if (publicationFence is null)
        {
            var unfenced = await cache.GetOrDefaultAsync<LeaderboardProjectionBundle?>(
                ProjectionKey(competitionId), null, token: ct);
            return IsExpired(unfenced) ? null : unfenced;
        }

        var published = await publicationFence.GetAsync(competitionId, ct);
        if (published is null)
            return null;
        var bundle = JsonSerializer.Deserialize<LeaderboardProjectionBundle>(
            published.Payload,
            JsonOptions);
        if (bundle is null || bundle.Scoreboard.Snapshot.Version != published.Fence)
            throw new InvalidOperationException("The fenced leaderboard payload is invalid.");
        if (IsExpired(bundle))
            return null;

        var cached = await cache.GetOrDefaultAsync<LeaderboardProjectionBundle?>(
            ProjectionKey(competitionId), null, token: ct);
        if (cached?.Scoreboard.Snapshot.Version != published.Fence)
            await cache.SetAsync(ProjectionKey(competitionId), bundle, token: ct);
        return bundle;
    }

    private bool IsExpired(LeaderboardProjectionBundle? bundle) =>
        bundle?.ValidUntil is { } validUntil && timeProvider.GetUtcNow() >= validUntil
        || bundle is not null && bundle.Scoreboard.Schema.Mode == GameMode.Awdp
            && bundle.AwdpRoundProjectionFormat != CurrentAwdpRoundProjectionFormat
        || bundle is not null && bundle.Scoreboard.Snapshot.DataScope == LeaderboardDataScope.Live
            && bundle.Scoreboard.Schema.Mode is GameMode.Ctf or GameMode.Awdp
            && bundle.Scoreboard.Snapshot.Teams.Any(team => team.Achievements is null);

    private async Task RepairFusionCacheAsync(Guid competitionId, CancellationToken ct)
    {
        if (publicationFence is null)
            return;
        var published = await publicationFence.GetAsync(competitionId, ct);
        if (published is null)
            return;
        var bundle = JsonSerializer.Deserialize<LeaderboardProjectionBundle>(
            published.Payload,
            JsonOptions);
        if (bundle is not null && bundle.Scoreboard.Snapshot.Version == published.Fence)
            await cache.SetAsync(ProjectionKey(competitionId), bundle, token: ct);
    }

    private async Task ReleaseProjectionLockAsync(Guid competitionId) =>
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_unlock(hashtextextended({competitionId.ToString("N")}, 0))",
            CancellationToken.None);

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
                Snapshot = candidate.Scoreboard.Snapshot with { Version = nextVersion },
                ParticipantView = candidate.Scoreboard.ParticipantView is { } participant
                    ? participant with
                    {
                        Snapshot = participant.Snapshot with { Version = nextVersion }
                    }
                    : null
            }
        };
    }

    private sealed record AwdScoreboardWindow(
        IReadOnlyList<LeaderboardAwdRoundFact> Rounds,
        int? StartRound,
        int? EndRound,
        int? LatestRound)
    {
        public static AwdScoreboardWindow Empty { get; } = new([], null, null, null);
    }

    private static LeaderboardProjectionBundle SetScoreboardVersion(
        LeaderboardProjectionBundle candidate,
        long version) => candidate with
        {
            Scoreboard = candidate.Scoreboard with
            {
                Snapshot = candidate.Scoreboard.Snapshot with { Version = version },
                ParticipantView = candidate.Scoreboard.ParticipantView is { } participant
                    ? participant with
                    {
                        Snapshot = participant.Snapshot with { Version = version }
                    }
                    : null
            }
        };
}
