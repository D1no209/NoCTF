using System.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Application.Observability;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Competitions.Modes;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Competitions.Webhooks;
using ZiggyCreatures.Caching.Fusion;
using NoCTF.Application.Messaging;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class FusionLeaderboardCache(
    NoCtfDbContext db,
    ILeaderboardProjectionEngine projectionEngine,
    ILeaderboardRefreshPublisher publisher,
    IFusionCacheProvider caches,
    ILeaderboardPublicationFence? publicationFence = null,
    TimeProvider? clock = null,
    LeaderboardSnapshotDeserializer? snapshotDeserializer = null,
    IPostCommitMessagePublisher? workPublisher = null,
    PublishedLeaderboardMemo? memo = null) : ILeaderboardCache, ILeaderboardSnapshotFactory
{
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.Leaderboards);
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    public async Task<ScoreboardProjection?> GetScoreboardAsync(Guid competitionId, CancellationToken ct) =>
        (await ReadPublishedBundleAsync(competitionId, ct))?.Scoreboard;

    public async Task<ScoreboardProjection?> GetFrozenScoreboardAsync(Guid competitionId, CancellationToken ct)
        => (await GetFrozenBundleAsync(competitionId, ct))?.Scoreboard;

    public async Task<ScoreboardProjection?> GetFrozenSpectatorScoreboardAsync(Guid competitionId, CancellationToken ct)
        => (await GetFrozenBundleAsync(competitionId, ct))?.Scoreboard;

    public async Task<WebhookScoreboardProjection?> GetWebhookScoreboardAsync(
        Guid competitionId, bool frozen, CancellationToken ct)
    {
        var bundle = frozen
            ? await GetFrozenBundleAsync(competitionId, ct)
            : await ReadPublishedBundleAsync(competitionId, ct);
        return bundle is null ? null : new(
            bundle.Scoreboard, bundle.SourceEventSequenceThrough);
    }

    public async Task<WebhookScoreboardProjection?> GetFrozenWebhookScoreboardAsync(
        Guid competitionId, DateTimeOffset frozenAt, CancellationToken ct)
    {
        var frozen = await db.CompetitionWebhookFrozenProjections.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompetitionId == competitionId
                && item.FrozenAt == frozenAt, ct);
        if (frozen is not null)
            return ReadFrozen(frozen);

        var configuredFreeze = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId)
            .Select(item => item.FrozenStartAt)
            .SingleOrDefaultAsync(ct);
        // An explicit visibility change captures its frozen view in the same
        // transaction, including when an organizer chooses a past freeze time.
        // Background recovery must not reconstruct an old snapshot from live names.
        if (configuredFreeze != frozenAt
            || (db.Database.CurrentTransaction is null
                && timeProvider.GetUtcNow() - frozenAt > TimeSpan.FromSeconds(5)))
            return null;

        var projection = await ProjectBundleAsync(
            competitionId, null, frozenAt, null, ct);
        if (projection is null)
            return null;
        var row = new CompetitionWebhookFrozenProjection
        {
            CompetitionId = competitionId,
            FrozenAt = frozenAt,
            CapturedAt = timeProvider.GetUtcNow(),
            SourceEventSequenceThrough = projection.SourceEventSequenceThrough,
            Payload = JsonSerializer.SerializeToUtf8Bytes(projection,
                CachedScoreboardJsonContext.Default.CachedScoreboardProjection)
        };
        db.CompetitionWebhookFrozenProjections.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return new(projection.Scoreboard, projection.SourceEventSequenceThrough);
        }
        catch (DbUpdateException) when (db.Database.CurrentTransaction is null)
        {
            db.Entry(row).State = EntityState.Detached;
            frozen = await db.CompetitionWebhookFrozenProjections.AsNoTracking()
                .SingleOrDefaultAsync(item => item.CompetitionId == competitionId
                    && item.FrozenAt == frozenAt, ct);
            if (frozen is null) throw;
            return ReadFrozen(frozen);
        }
    }

    private static WebhookScoreboardProjection ReadFrozen(
        CompetitionWebhookFrozenProjection row)
    {
        var projection = JsonSerializer.Deserialize(row.Payload,
            CachedScoreboardJsonContext.Default.CachedScoreboardProjection)
            ?? throw new InvalidOperationException("Frozen webhook projection is invalid.");
        return new(projection.Scoreboard, row.SourceEventSequenceThrough);
    }

    private async Task<CachedScoreboardProjection?> GetFrozenBundleAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var access = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => new { competition.FrozenStartAt, competition.ScoreboardAccessRevision })
            .SingleOrDefaultAsync(ct);
        if (access?.FrozenStartAt is not { } frozenAt) return null;
        var captured = await db.CompetitionWebhookFrozenProjections.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompetitionId == competitionId && item.FrozenAt == frozenAt, ct);
        if (captured is null) return null;
        var bundle = JsonSerializer.Deserialize(captured.Payload,
            CachedScoreboardJsonContext.Default.CachedScoreboardProjection)
            ?? throw new InvalidOperationException("Frozen scoreboard projection is invalid.");
        if (bundle.AccessRevision == access.ScoreboardAccessRevision) return bundle;
        var challenges = (await db.CompetitionChallenges.AsNoTracking().Where(item => item.CompetitionId == competitionId && item.IsPublished)
            .Select(item => item.Id).ToArrayAsync(ct)).ToHashSet();
        var teams = await db.Teams.AsNoTracking().Where(item => item.CompetitionId == competitionId && item.DeletedAt == null
            && !item.IsBanned && item.RegistrationStatus == TeamRegistrationStatus.Approved)
            .ToDictionaryAsync(item => item.Id, item => item.TrackKey, ct);
        var filtered = ScoreboardAudienceProjection.FilterCurrentAccess(bundle.Scoreboard, challenges, teams);
        return bundle with { Scoreboard = filtered with { ParticipantView = ScoreboardAudienceView.From(filtered) } };
    }

    public async Task<ScoreboardProjection?> CreateScoreboardAsync(
        Guid competitionId,
        DateTimeOffset projectedAt,
        CancellationToken ct) =>
        (await ProjectBundleAsync(competitionId, null, projectedAt, null, ct))?.Scoreboard;

    public async Task<ScoreboardProjection?> CreateAwdScoringPreviewAsync(Guid competitionId, DateTimeOffset projectedAt,
        NoCTF.Application.Competitions.Awd.AwdScoringProjectionOverride candidate, CancellationToken ct) =>
        (await ProjectBundleAsync(competitionId, null, projectedAt, null, ct, awdScoringOverride: candidate))?.Scoreboard;

    public async Task<ScoreboardProjection?> CreateTimingPreviewAsync(Guid competitionId, DateTimeOffset projectedAt,
        NoCTF.Application.Challenges.Timing.ChallengeTimingProjectionOverride candidate, CancellationToken ct) =>
        (await ProjectBundleAsync(competitionId, null, projectedAt, null, ct, candidate))?.Scoreboard;

    public async Task<ScoreboardProjection?> CreateScoreboardWindowAsync(
        Guid competitionId,
        int endingRound,
        DateTimeOffset projectedAt,
        CancellationToken ct) =>
        (await ProjectBundleAsync(competitionId, null, projectedAt, endingRound, ct))?.Scoreboard;

    private async Task<CachedScoreboardProjection?> ProjectBundleAsync(
        Guid competitionId,
        CompetitionModeConfiguration? competitionConfiguration,
        DateTimeOffset projectedAt,
        int? scoreboardRoundWindowEnd,
        CancellationToken ct,
        NoCTF.Application.Challenges.Timing.ChallengeTimingProjectionOverride? timingOverride = null,
        NoCTF.Application.Competitions.Awd.AwdScoringProjectionOverride? awdScoringOverride = null)
    {
        await using var inputTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
            : null;
        var competition = await db.Competitions.AsNoTracking().AsSplitQuery()
            .SingleOrDefaultAsync(candidate => candidate.Id == competitionId, ct);
        if (competition is null)
            return null;
        if (!CompetitionModeCapabilities.For(competition.Mode).OrdinaryScoreboard)
            return null;
        // Capture the checkpoint before reading projection inputs. A later
        // checkpoint could acknowledge a newly committed event whose facts
        // were not included by the earlier queries.
        var sourceEventSequenceThrough = await db.CompetitionWebhookOutboxEvents
            .AsNoTracking()
            .Where(item => item.CompetitionId == competitionId
                && (competition.FrozenStartAt == null
                    || projectedAt != competition.FrozenStartAt.Value
                    || item.DomainEventCreatedAt <= projectedAt))
            .Select(item => (long?)item.Sequence)
            .MaxAsync(ct) ?? 0;
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
            competition.Tracks);
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
            var track = CtfCompletionEligibility.Track(trackConfiguration, team.TrackKey);
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
            .AsSplitQuery()
            .ToListAsync(ct);
        var scorePresets = competition.Mode == GameMode.Ctf
            ? await db.Set<NoCTF.Domain.Competitions.Scoring.CompetitionScorePreset>().AsNoTracking()
                .Where(item => item.CompetitionId == competitionId).ToArrayAsync(ct)
            : [];
        var templateIds = challengeEntities.Select(instance => instance.ChallengeId).ToArray();
        var templates = await db.Challenges.AsNoTracking()
            .Where(template => templateIds.Contains(template.Id))
            .AsSplitQuery()
            .ToDictionaryAsync(template => template.Id, ct);
        var challenges = challengeEntities
            .Where(instance => templates.ContainsKey(instance.ChallengeId))
            .Select(instance => new LeaderboardChallengeFact(
                instance.Id,
                instance.Direction?.Name ?? templates[instance.ChallengeId].Direction,
                instance.CustomTitle ?? templates[instance.ChallengeId].Title,
                false,
                instance.Rules,
                instance.Order,
                instance.IsPublished,
                templates[instance.ChallengeId].Definition,
                templates[instance.ChallengeId].Definition is CtfChallengeDefinition ctf
                    ? ctf.InteractionKind
                    : CtfInteractionKind.FlagSubmission,
                instance.Direction?.Icon, timingOverride != null && instance.Id == timingOverride.CompetitionChallengeId
                    ? timingOverride.Timing : ChallengeTiming.From(instance),
                NoCTF.Domain.Competitions.Scoring.CompetitionScorePreset.Match(instance.Tags.Select(tag => tag.Name), scorePresets)))
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
            return new CompetitionLifecycleTransition
            {
                Id = @event.Id,
                CompetitionId = @event.CompetitionId,
                From = @event.PreviousCompetitionStatus
                    ?? throw new InvalidOperationException("Lifecycle event has no previous status."),
                To = @event.CompetitionStatus
                    ?? throw new InvalidOperationException("Lifecycle event has no current status."),
                ActorId = @event.ActorUserId,
                Reason = @event.Reason,
                Automatic = @event.Automatic,
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
            competitionConfiguration ?? competition.ModeConfiguration,
            competition.StartAt,
            competition.EndAt,
            lifecycle,
            projectedAt,
            hintCosts,
            teamFacts,
            scoreboardRoundWindowEnd,
            awdWindow.Rounds,
            challenges,
            ct, timingOverride, awdScoringOverride);
        var actorIds = factRows.Aggregate
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
        var aggregateFacts = EnrichActors(factRows.Aggregate);
        var scoreboardFacts = EnrichActors(factRows.Scoreboard);

        var selectedRoundWindowEnd = competition.Mode == GameMode.Awd
            ? awdWindow.EndRound
            : scoreboardRoundWindowEnd;
        var theory = await NoCTF.Infrastructure.Theory.Scoring.TheoryScoreReader.ReadAsync(db, competitionId, projectedAt, ct);
        var rankingRound = competition.Mode == GameMode.Awd
            ? await db.AwdRounds.AsNoTracking().Where(round => round.CompetitionId == competitionId && round.StartsAt <= projectedAt)
                .OrderByDescending(round => round.Number).FirstOrDefaultAsync(ct) : null;
        var projectionInput = new LeaderboardProjectionInput(
            competitionId,
            competition.Mode,
            teamFacts,
            aggregateFacts,
            challenges,
            competitionConfiguration ?? competition.ModeConfiguration,
            competition.StartAt,
            lifecycle,
            awdWindow.Rounds,
            projectedAt,
            competitionStatusAtProjection,
            scoreboardFacts,
            selectedRoundWindowEnd,
            awdWindow.LatestRound,
            factRows.AwdAggregates,
            await db.WriteUpUnlockReceipts.AsNoTracking().Where(x => x.CompetitionId == competitionId && x.UnlockedAt <= projectedAt)
                .Select(x => new LeaderboardWriteUpUnlock(x.GameplayFactId, x.TeamId, x.CompetitionChallengeId, x.DeductionPercent, x.UnlockedAt)).ToArrayAsync(ct),
            factRows.AwdRoundScores,
            competition.Mode == GameMode.Awd ? await db.AwdRoundRules.AsNoTracking()
                .Where(rule => rule.CompetitionId == competitionId && rule.EffectiveAt <= projectedAt)
                .OrderByDescending(rule => rule.EffectiveAt).ThenByDescending(rule => rule.Revision)
                .Select(rule => rule.Options.MinimumTotalScore).FirstOrDefaultAsync(ct) : null,
            rankingRound is null ? null : AwdTieBreakPolicy.Resolve(rankingRound.TieBreakers.OrderBy(item => item.Position)
                 .Select(item => new AwdTieBreakCriterion(item.Field, item.Direction))),
            theory.Scores);
        var spectator = await SpectatorFrameReader.ReadAsync(db, competition.Title, projectionInput, ct,
            timingOverride, rankingRound?.Id);
        if (inputTransaction is not null)
        {
            await inputTransaction.CommitAsync(ct);
            await inputTransaction.DisposeAsync();
        }
        var scoreboard = projectionEngine.Project(projectionInput);
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
            && aggregateFacts.All(IsParticipantVisible)
            && scoreboardFacts.All(IsParticipantVisible)
            && awdWindow.Rounds.All(round =>
                publishedChallengeIds.Contains(round.CompetitionChallengeId))
            && (factRows.AwdAggregates is null || factRows.AwdAggregates.All(fact =>
                publishedChallengeIds.Contains(fact.CompetitionChallengeId)))
            && (projectionInput.WriteUpUnlocks ?? []).All(fact => publishedChallengeIds.Contains(fact.CompetitionChallengeId));
        if (participantProjectionMatchesFull)
        {
            participantScoreboard = scoreboard;
        }
        else
        {
            var participantInput = projectionInput with
            {
                Challenges = challenges.Where(challenge => challenge.IsPublished).ToArray(),
                GameplayFacts = aggregateFacts.Where(IsParticipantVisible).ToArray(),
                ScoreboardGameplayFacts = scoreboardFacts.Where(IsParticipantVisible).ToArray(),
                AwdRounds = awdWindow.Rounds
                    .Where(round => publishedChallengeIds.Contains(round.CompetitionChallengeId))
                    .ToArray(),
                AwdAggregates = factRows.AwdAggregates?
                    .Where(fact => publishedChallengeIds.Contains(fact.CompetitionChallengeId))
                    .ToArray(),
                AwdRoundScores = factRows.AwdRoundScores?.Where(fact => publishedChallengeIds.Contains(fact.CompetitionChallengeId)).ToArray(),
                WriteUpUnlocks = (projectionInput.WriteUpUnlocks ?? []).Where(fact => publishedChallengeIds.Contains(fact.CompetitionChallengeId)).ToArray()
            };
            participantScoreboard = AddResponseMetadata(
                projectionEngine.Project(participantInput));
        }
        scoreboard = scoreboard with
        {
            Spectator = spectator,
            ParticipantView = ScoreboardAudienceView.From(participantScoreboard)
        };
        return new(
            scoreboard,
            competition.Mode == GameMode.Awdp
                && competitionStatusAtProjection == CompetitionStatus.Running
                ? scoreboard.Schema.Rounds.SingleOrDefault(round => round.State == ScoreboardRoundState.Running)?.EndAt
                : null,
            sourceEventSequenceThrough)
        {
            TimingRevisions = challengeEntities.ToDictionary(x => x.Id, x => x.TimingRevision),
            AccessRevision = competition.ScoreboardAccessRevision
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

        var roundMetadata = db.AwdRounds.AsNoTracking().Where(round => round.StartsAt <= projectedAt
            && db.AwdRoundRules.Any(rule => rule.RoundId == round.Id && competitionChallengeIds.Contains(rule.CompetitionChallengeId)));
        var latestRound = await roundMetadata.Select(round => (int?)round.Number).MaxAsync(ct) ?? 0;
        if (latestRound == 0) return AwdScoreboardWindow.Empty;
        var endRound = Math.Clamp(requestedEndingRound ?? latestRound, 1, latestRound);
        var startRound = Math.Max(1, endRound - ScoreboardRoundWindow.DefaultSize + 1);
        var selected = await roundMetadata.Where(round => round.Number >= startRound && round.Number <= endRound)
            .OrderBy(round => round.Number).ToArrayAsync(ct);
        var selectedRoundIds = selected.Select(round => round.Id).ToArray();
        var rules = await db.AwdRoundRules.AsNoTracking().Include(rule => rule.Teams)
            .Where(rule => selectedRoundIds.Contains(rule.RoundId) && competitionChallengeIds.Contains(rule.CompetitionChallengeId))
            .ToArrayAsync(ct);
        var byId = selected.ToDictionary(round => round.Id);
        var rounds = rules.SelectMany(rule => rule.Teams.Select(team => new LeaderboardAwdRoundFact(
            rule.CompetitionChallengeId, team.TeamId, rule.RoundId, byId[rule.RoundId].StartsAt,
            byId[rule.RoundId].EndsAt, byId[rule.RoundId].Number)))
            .DistinctBy(round => (round.CompetitionChallengeId, round.TeamId, round.RoundId)).ToArray();
        return new(rounds, startRound, endRound, latestRound);
    }

    public Task RefreshAsync(Guid competitionId, CancellationToken ct) => RefreshCoreAsync(competitionId, ct);

    public async Task<bool> NeedsRefreshAsync(Guid competitionId, CancellationToken ct) =>
        publicationFence is null || await publicationFence.HasPendingAsync(competitionId, ct)
            || await GetPublishedBundleAsync(competitionId, ct) is null;

    public async Task RequestRefreshAsync(Guid competitionId, CancellationToken ct)
    {
        if (workPublisher is null) throw new InvalidOperationException("A leaderboard refresh publisher is required.");
        ct.ThrowIfCancellationRequested();
        await InvalidateAsync(competitionId, ct);
        await workPublisher.PublishAsync(new ProjectLeaderboard(competitionId, LeaderboardRefreshMode.IfMissing));
        await workPublisher.FlushCommittedMessagesAsync();
    }

    public async Task InvalidateAccessAsync(Guid competitionId, CancellationToken ct)
    {
        memo?.Invalidate(competitionId);
        if (publicationFence is not null)
            await publicationFence.InvalidateAccessAsync(competitionId, timeProvider.GetUtcNow().UtcTicks, ct);
    }

    private async Task RefreshCoreAsync(Guid competitionId, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMinutes(2));
        ct = budget.Token;
        var publicationPhase = false;
        CachedScoreboardProjection? response = null;
        LeaderboardBuildTicket? ticket = null;
        try
        {
            if (publicationFence is not null)
                ticket = await publicationFence.BeginBuildAsync(
                    competitionId, timeProvider.GetUtcNow().UtcTicks, ct);
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
            publicationPhase = true;
            if (!await HasCurrentAccessAsync(competitionId, response, ct)) return;

            if (publicationFence is null)
            {
                var previous = await cache.GetOrDefaultAsync<CachedScoreboardProjection?>(
                    ProjectionKey(competitionId), null, token: ct);
                response = AdvanceScoreboardVersion(response, previous);
            }
            else
            {
                response = SetScoreboardVersion(response,
                    ticket?.Version ?? throw new InvalidOperationException(
                        "Scoreboard publication has no fencing token."));
            }

            if (publicationFence is not null)
            {
                var payload = JsonSerializer.Serialize(response,
                    CachedScoreboardJsonContext.Default.CachedScoreboardProjection);
                var accepted = await publicationFence.TryPublishAsync(
                    competitionId,
                    ticket!,
                    response.SourceEventSequenceThrough,
                    payload,
                    ct);
                if (!accepted)
                    return;
            }

            if (publicationFence is null)
                await cache.SetAsync(ProjectionKey(competitionId), response, new FusionCacheEntryOptions().SetDurationInfinite(), token: ct);
            else if (!await publicationFence.IsCurrentAsync(
                competitionId, ticket!.Version, ct))
            {
                return;
            }

            if (memo is not null)
            {
                memo.Published(competitionId, response.Scoreboard.Snapshot.Version);
                memo.Set(competitionId, response);
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

    private static string ProjectionKey(Guid competitionId) => $"scoreboard:v3:{competitionId:N}";
    private static string FailureKey(Guid competitionId) => $"leaderboard:v3:{competitionId:N}:last-failure";

    private async Task<CachedScoreboardProjection?> ReadPublishedBundleAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        using var measurement = NoCtfTelemetry.MeasureLeaderboardRead(LeaderboardReadPerformanceStage.Read);
        return await GetPublishedBundleAsync(competitionId, ct);
    }

    private async Task<CachedScoreboardProjection?> GetPublishedBundleAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        using var measurement = NoCtfTelemetry.MeasureLeaderboardRead(LeaderboardReadPerformanceStage.PublishedBundle);
        if (publicationFence is null)
        {
            var unfenced = await cache.GetOrDefaultAsync<CachedScoreboardProjection?>(
                ProjectionKey(competitionId), null, token: ct);
            return unfenced is not null && await HasCurrentAccessAsync(competitionId, unfenced, ct) ? unfenced : null;
        }

        if (memo is not null && memo.TryGet(competitionId, out var memoized))
            return memoized;
        var published = await publicationFence.GetAsync(competitionId, ct);
        if (published is null)
            return null;
        var bundle = snapshotDeserializer is null
            ? LeaderboardSnapshotDeserializer.Deserialize(competitionId, published)
            : await snapshotDeserializer.ReadAsync(competitionId, published, ct);
        if (!await HasCurrentAccessAsync(competitionId, bundle, ct))
            return null;
        memo?.Set(competitionId, bundle);
        return bundle;
    }

    private async Task<bool> HasCurrentAccessAsync(Guid competitionId, CachedScoreboardProjection bundle, CancellationToken ct) =>
        await db.Competitions.AsNoTracking().Where(item => item.Id == competitionId)
            .Select(item => (Guid?)item.ScoreboardAccessRevision).SingleOrDefaultAsync(ct) == bundle.AccessRevision;

    private static CachedScoreboardProjection AdvanceScoreboardVersion(
        CachedScoreboardProjection candidate,
        CachedScoreboardProjection? previous)
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

    private static CachedScoreboardProjection SetScoreboardVersion(
        CachedScoreboardProjection candidate,
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

internal sealed record CachedScoreboardProjection(
    ScoreboardProjection Scoreboard,
    DateTimeOffset? ValidUntil = null,
    long SourceEventSequenceThrough = 0)
{
    public Guid AccessRevision { get; init; }
    public IReadOnlyDictionary<Guid, Guid> TimingRevisions { get; init; } = new Dictionary<Guid, Guid>();
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CachedScoreboardProjection))]
internal partial class CachedScoreboardJsonContext : JsonSerializerContext;
