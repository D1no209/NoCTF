using System.Net;
using System.Net.Http.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.API.Security;

namespace NoCTF.Tests.Unit.API;

public sealed class LeaderboardEndpointTests
{
    [Test]
    public async Task Cached_snapshot_is_returned_without_requesting_projection()
    {
        var competitionId = Guid.CreateVersion7();
        var messages = new RecordingMessagePublisher();
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(),
            messages);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(messages.ProjectedCompetitionIds).Count()
            .IsEqualTo(0);
    }

    [Test]
    public async Task Missing_snapshot_returns_accepted_with_retry_after()
    {
        var competitionId = Guid.CreateVersion7();
        var messages = new RecordingMessagePublisher();
        var leaderboard = new CachedLeaderboard(missing: true);
        await using var app = await CreateApplicationAsync(
            competitionId,
            leaderboard,
            messages);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(response.Headers.RetryAfter?.Delta)
            .IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(messages.ProjectedCompetitionIds).IsEmpty();
        await Assert.That(leaderboard.InvalidatedCompetitionIds)
            .IsEquivalentTo([competitionId]);
    }

    [Test]
    public async Task Projection_failure_returns_service_unavailable_without_requeue()
    {
        var competitionId = Guid.CreateVersion7();
        var messages = new RecordingMessagePublisher();
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(
                missing: true,
                lastFailureAt: DateTimeOffset.UtcNow),
            messages);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(messages.ProjectedCompetitionIds).IsEmpty();
    }

    [Test]
    public async Task Unknown_competition_returns_not_found()
    {
        var competitionId = Guid.CreateVersion7();
        var messages = new RecordingMessagePublisher();
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(),
            messages);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{Guid.CreateVersion7()}/leaderboard");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(messages.ProjectedCompetitionIds).IsEmpty();
    }

    [Test]
    public async Task Blackout_returns_hidden_empty_projection_without_queueing_work()
    {
        var competitionId = Guid.CreateVersion7();
        var messages = new RecordingMessagePublisher();
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(),
            messages,
            CompetitionLeaderboardVisibility.Blackout,
            LeaderboardDataScope.Hidden);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");
        var body = await response.Content.ReadFromJsonAsync<ScoreboardSnapshotResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Visibility)
            .IsEqualTo(LeaderboardVisibilityProtocol.Blackout);
        await Assert.That(body.DataScope).IsEqualTo(LeaderboardDataScopeProtocol.Hidden);
        await Assert.That(body.Teams).IsEmpty();
        await Assert.That(messages.ProjectedCompetitionIds).IsEmpty();
    }

    [Test]
    public async Task Frozen_projection_uses_persisted_snapshot_without_live_refresh()
    {
        var competitionId = Guid.CreateVersion7();
        var messages = new RecordingMessagePublisher();
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(frozen: true),
            messages,
            CompetitionLeaderboardVisibility.Frozen,
            LeaderboardDataScope.Frozen);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");
        var body = await response.Content.ReadFromJsonAsync<ScoreboardSnapshotResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Visibility)
            .IsEqualTo(LeaderboardVisibilityProtocol.Frozen);
        await Assert.That(body.DataScope).IsEqualTo(LeaderboardDataScopeProtocol.Frozen);
        await Assert.That(messages.ProjectedCompetitionIds).IsEmpty();
    }

    [Test]
    public async Task Catalog_and_schema_hide_unpublished_challenges_and_remap_columns()
    {
        var competitionId = Guid.CreateVersion7();
        var publishedId = Guid.CreateVersion7();
        var unpublishedId = Guid.CreateVersion7();
        var projection = CreateProjection(
            competitionId,
            [
                new(publishedId, "Published", "PWN", "PWN", 1, true, 2),
                new(unpublishedId, "Draft", "WEB", "WEB", 2, false, 3)
            ],
            [new(4, publishedId, null), new(9, unpublishedId, null)],
            []);
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(projection: projection),
            new RecordingMessagePublisher());
        using var client = app.GetTestClient();

        using var catalogResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/challenges");
        using var schemaResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/schema");
        var catalog = await catalogResponse.Content
            .ReadFromJsonAsync<ScoreboardChallengeCatalogResponse>();
        var schema = await schemaResponse.Content
            .ReadFromJsonAsync<ScoreboardSchemaResponse>();

        await Assert.That(catalogResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(schemaResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(catalog!.Items.Select(item => item.Id))
            .IsEquivalentTo([publishedId]);
        await Assert.That(schema!.Columns).Count().IsEqualTo(1);
        await Assert.That(schema.Columns[0].Index).IsEqualTo(0);
        await Assert.That(schema.Columns[0].CompetitionChallengeId).IsEqualTo(publishedId);
        await Assert.That(schema.ChallengeCatalogRevision).IsEqualTo(catalog.Revision);
    }

    [Test]
    public async Task Hidden_schema_preserves_the_real_game_mode_without_exposing_columns()
    {
        var competitionId = Guid.CreateVersion7();
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(),
            new RecordingMessagePublisher(),
            CompetitionLeaderboardVisibility.Blackout,
            LeaderboardDataScope.Hidden,
            gameMode: GameMode.Awdp);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/schema");
        var schema = await response.Content.ReadFromJsonAsync<ScoreboardSchemaResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(schema!.Mode).IsEqualTo(GameModeProtocol.Awdp);
        await Assert.That(schema.Columns).IsEmpty();
        await Assert.That(schema.Rounds).IsEmpty();
    }

    [Test]
    public async Task Slot_detail_uses_signed_team_scoped_cursor_and_returns_authoritative_totals()
    {
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var firstTeamId = Guid.CreateVersion7();
        var secondTeamId = Guid.CreateVersion7();
        var firstActorId = Guid.CreateVersion7();
        var secondActorId = Guid.CreateVersion7();
        var thirdActorId = Guid.CreateVersion7();
        var occurredAt = DateTimeOffset.UtcNow;
        var detailReader = new RecordingSlotDetailReader(
        [
            new(Guid.CreateVersion7(), GameplayFactKind.FlagAttempt, GameplayFactState.Completed,
                GameplayFactResult.Correct, firstActorId, "First player", null, occurredAt),
            new(Guid.CreateVersion7(), GameplayFactKind.FlagAttempt, GameplayFactState.Completed,
                GameplayFactResult.Wrong, secondActorId, "Second player", null, occurredAt.AddSeconds(-1)),
            new(Guid.CreateVersion7(), GameplayFactKind.FlagAttempt, GameplayFactState.Completed,
                GameplayFactResult.Duplicate, thirdActorId, "Third player", null, occurredAt.AddSeconds(-2))
        ]);
        var slot = new ScoreboardSlot(
            0,
            ScoreboardScoreState.Settled,
            500,
            100,
            400,
            3,
            [new(ScoreboardBreakdownKind.Solve, 1, 3, 500, 100, 400)],
            []);
        var projection = CreateProjection(
            competitionId,
            [new(challengeId, "Challenge", "PWN", "PWN", 1, true, 1)],
            [new(0, challengeId, null)],
            [
                new(firstTeamId, "Alpha", "default", 1, ScoreboardRankingState.Eligible, 400, 0, [], [slot]),
                new(secondTeamId, "Beta", "default", 2, ScoreboardRankingState.Eligible, 400, 0, [], [slot])
            ]);
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(projection: projection),
            new RecordingMessagePublisher(),
            details: detailReader);
        using var client = app.GetTestClient();
        var route = $"/api/v1/competitions/{competitionId}/leaderboard/teams/{firstTeamId}/columns/0?limit=2";

        using var first = await client.GetAsync(route);
        var page = await first.Content.ReadFromJsonAsync<ScoreboardSlotDetailResponse>();

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(page!.Items).Count().IsEqualTo(2);
        await Assert.That(page.Actors.Select(actor => actor.UserId))
            .IsEquivalentTo([firstActorId, secondActorId]);
        await Assert.That(page.Items.Select(item => item.ActorIndex))
            .IsEquivalentTo(new int?[] { 0, 1 });
        await Assert.That(page.NextCursor).IsNotNull();
        await Assert.That(page.EarnedPoints).IsEqualTo(500);
        await Assert.That(page.DeductedPoints).IsEqualTo(100);
        await Assert.That(page.NetPoints).IsEqualTo(400);
        await Assert.That(page.Items.All(item => item.EarnedPoints == 0)).IsTrue();
        await Assert.That(page.Items.All(item => item.DeductedPoints == 0)).IsTrue();
        await Assert.That(page.Items.All(item => item.NetPoints == 0)).IsTrue();
        await Assert.That(detailReader.Queries.Single().Limit).IsEqualTo(3);
        await Assert.That(detailReader.Queries.Single().DataAsOf)
            .IsEqualTo(projection.Snapshot.GeneratedAt);

        var cursor = Uri.EscapeDataString(page.NextCursor!);
        using var otherTeam = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/teams/{secondTeamId}/columns/0?limit=2&cursor={cursor}");
        var tampered = Uri.EscapeDataString(page.NextCursor![..^1]
            + (page.NextCursor[^1] == 'A' ? "B" : "A"));
        using var tamperedResponse = await client.GetAsync($"{route}&cursor={tampered}");

        await Assert.That(otherTeam.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(tamperedResponse.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Public_snapshot_and_slot_detail_hide_internal_track_teams_and_actors()
    {
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var publicTeamId = Guid.CreateVersion7();
        var internalTeamId = Guid.CreateVersion7();
        var publicActorId = Guid.CreateVersion7();
        var internalActorId = Guid.CreateVersion7();
        var slot = new ScoreboardSlot(
            0,
            ScoreboardScoreState.Provisional,
            1,
            0,
            1,
            1,
            [],
            [new(Guid.CreateVersion7(), ScoreboardEntryKind.Solve, ScoreboardEntryOutcome.Succeeded,
                0, null, DateTimeOffset.UtcNow, null, 1, 0, 1)]);
        var internalSlot = slot with
        {
            Entries = [slot.Entries[0] with { Id = Guid.CreateVersion7(), ActorIndex = 1 }]
        };
        var projection = CreateProjection(
            competitionId,
            [new(challengeId, "Challenge", "PWN", "PWN", 1, true, 1)],
            [new(0, challengeId, null)],
            [
                new(publicTeamId, "Public", "default", 1, ScoreboardRankingState.Eligible, 1, 0, [], [slot]),
                new(internalTeamId, "Internal", "staff", null, ScoreboardRankingState.Disqualified, 1, 0, [], [internalSlot])
            ],
            [
                new(0, publicActorId, "Public player"),
                new(1, internalActorId, "Internal player")
            ]);
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(projection: projection),
            new RecordingMessagePublisher(),
            trackStore: new InternalTrackStore(competitionId));
        using var client = app.GetTestClient();

        using var leaderboardResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");
        var leaderboard = await leaderboardResponse.Content.ReadFromJsonAsync<ScoreboardSnapshotResponse>();
        using var detailResponse = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard/teams/{internalTeamId}/columns/0");

        await Assert.That(leaderboardResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(leaderboard!.Teams.Select(team => team.TeamId)).IsEquivalentTo([publicTeamId]);
        await Assert.That(leaderboard.Actors.Select(actor => actor.UserId)).IsEquivalentTo([publicActorId]);
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Slot_detail_cursor_is_invalidated_when_frozen_snapshot_identity_changes()
    {
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var occurredAt = DateTimeOffset.UtcNow;
        var facts = new RecordingSlotDetailReader([
            new(Guid.CreateVersion7(), GameplayFactKind.FlagAttempt, GameplayFactState.Completed,
                GameplayFactResult.Correct, null, null, null, occurredAt),
            new(Guid.CreateVersion7(), GameplayFactKind.FlagAttempt, GameplayFactState.Completed,
                GameplayFactResult.Wrong, null, null, null, occurredAt.AddSeconds(-1))
        ]);
        var slot = new ScoreboardSlot(
            0, ScoreboardScoreState.Settled, 1, 0, 1, 2, [], []);
        var first = CreateProjection(
            competitionId,
            [new(challengeId, "Challenge", "PWN", "PWN", 1, true, 1)],
            [new(0, challengeId, null)],
            [new(teamId, "Alpha", "default", 1, ScoreboardRankingState.Eligible, 1, 0, [], [slot])]);
        first = first with
        {
            Snapshot = first.Snapshot with { DataAsOf = occurredAt, Version = 17 }
        };
        var second = first with
        {
            Snapshot = first.Snapshot with
            {
                DataAsOf = occurredAt.AddSeconds(1),
                Version = 18
            }
        };
        await using var app = await CreateApplicationAsync(
            competitionId,
            new SwitchingLeaderboard(first, second),
            new RecordingMessagePublisher(),
            CompetitionLeaderboardVisibility.Frozen,
            LeaderboardDataScope.Frozen,
            details: facts);
        using var client = app.GetTestClient();
        var route = $"/api/v1/competitions/{competitionId}/leaderboard/teams/{teamId}/columns/0?limit=1";

        using var firstResponse = await client.GetAsync(route);
        var page = await firstResponse.Content.ReadFromJsonAsync<ScoreboardSlotDetailResponse>();
        using var secondResponse = await client.GetAsync(
            $"{route}&cursor={Uri.EscapeDataString(page!.NextCursor!)}");

        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(page.NextCursor).IsNotNull();
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(facts.Queries).Count().IsEqualTo(1);
        await Assert.That(facts.Queries[0].DataAsOf).IsEqualTo(occurredAt);
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        Guid competitionId,
        ILeaderboardCache leaderboard,
        IBackendMessagePublisher messages,
        CompetitionLeaderboardVisibility visibility = CompetitionLeaderboardVisibility.Normal,
        LeaderboardDataScope dataScope = LeaderboardDataScope.Live,
        GameMode gameMode = GameMode.Ctf,
        IScoreboardSlotDetailReader? details = null,
        ICompetitionTrackStore? trackStore = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Pagination:SigningKey"] = "leaderboard-endpoint-tests-signing-key"
        });
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(GetLeaderboardEndpoint).Assembly];
            options.Filter = type => type == typeof(GetLeaderboardEndpoint)
                || type == typeof(GetScoreboardChallengeCatalogEndpoint)
                || type == typeof(GetScoreboardSchemaEndpoint)
                || type == typeof(GetScoreboardSlotDetailEndpoint)
                || type == typeof(GetScoreboardSlotDetailValidator);
        });
        builder.Services.SwaggerDocument();
        builder.Services.AddSingleton(leaderboard);
        builder.Services.AddSingleton(messages);
        builder.Services.AddSingleton<ICompetitionVisibilityAccess>(
            new PublicVisibilityAccess(competitionId, gameMode, visibility, dataScope));
        builder.Services.AddSingleton<GetCompetitionTracks>();
        builder.Services.AddSingleton<ICompetitionTrackStore>(
            trackStore ?? new DefaultTrackStore(competitionId));
        builder.Services.AddSingleton<ICompetitionModerationAuthorizer>(new NoStaffAccess());
        builder.Services.AddSingleton<IUserContext>(new AnonymousUserContext());
        builder.Services.AddSingleton<NoCTF.API.Pagination.SignedKeysetCursor>();
        builder.Services.AddSingleton<IScoreboardSlotDetailReader>(
            details ?? new RecordingSlotDetailReader([]));

        var app = builder.Build();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class PublicVisibilityAccess(
        Guid competitionId,
        GameMode gameMode,
        CompetitionLeaderboardVisibility visibility,
        LeaderboardDataScope dataScope)
        : ICompetitionVisibilityAccess
    {
        public Task<CompetitionVisibilityAccessDecision?> ResolveAsync(
            Guid userId,
            Guid requestedCompetitionId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionVisibilityAccessDecision?>(
                requestedCompetitionId == competitionId
                    ? new(
                        gameMode,
                        NoCTF.Domain.Competitions.CompetitionStatus.Running,
                        visibility,
                        dataScope,
                        2)
                    : null);
    }

    private sealed class AnonymousUserContext : IUserContext
    {
        public Guid UserId => Guid.Empty;
        public bool IsAdministrator => false;
    }

    private sealed class DefaultTrackStore(Guid competitionId) : ICompetitionTrackStore
    {
        public Task<CompetitionTracksView?> GetAsync(
            Guid requestedCompetitionId,
            Guid? viewerUserId,
            bool includeInternal,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionTracksView?>(requestedCompetitionId == competitionId
                ? new CompetitionTracksView(
                    competitionId,
                    GameMode.Ctf,
                    CompetitionStatus.Running,
                    0,
                    true,
                    [new CompetitionTrackView(
                        CompetitionTrackConfiguration.DefaultTrackKey,
                        "Default",
                        true,
                        true,
                        false,
                        true,
                        true,
                        true,
                        true,
                        true)])
                : null);

        public Task<NoCTF.Application.Common.OperationResult<CompetitionTracksView, CompetitionTrackFailureCode>> UpdateAsync(
            UpdateCompetitionTracksCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NoCTF.Application.Common.OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>> AssignAsync(
            AssignTeamTrackCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoStaffAccess : ICompetitionModerationAuthorizer
    {
        public Task<bool> CanModerateAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<bool> CanJudgeAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<bool> CanObserveAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<bool> CanReadHistoricalAuditAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class CachedLeaderboard(
        bool missing = false,
        bool frozen = false,
        DateTimeOffset? lastFailureAt = null,
        ScoreboardProjection? projection = null) : ILeaderboardCache
    {
        public List<Guid> InvalidatedCompetitionIds { get; } = [];

        public Task<LeaderboardResponse?> GetAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LeaderboardResponse?>(
                missing
                    ? null
                    : new(competitionId, DateTimeOffset.UtcNow, []));

        public Task<LeaderboardResponse?> GetFrozenAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LeaderboardResponse?>(frozen
                ? new LeaderboardResponse(
                    competitionId,
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    [])
                {
                    Visibility = CompetitionLeaderboardVisibility.Frozen,
                    DataScope = LeaderboardDataScope.Frozen,
                    DataAsOf = DateTimeOffset.UtcNow.AddMinutes(-5)
                }
                : null);

        public Task<ScoreboardProjection?> GetScoreboardAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ScoreboardProjection?>(missing
                ? null
                : projection ?? CreateScoreboard(competitionId, frozen: false));

        public Task<ScoreboardProjection?> GetFrozenScoreboardAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ScoreboardProjection?>(frozen
                ? projection ?? CreateScoreboard(competitionId, frozen: true)
                : null);

        public Task<LeaderboardCacheStatus> GetStatusAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LeaderboardCacheStatus(lastFailureAt));

        public Task RefreshAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task InvalidateAsync(
            Guid competitionId,
            CancellationToken cancellationToken)
        {
            InvalidatedCompetitionIds.Add(competitionId);
            return Task.CompletedTask;
        }

        private static ScoreboardProjection CreateScoreboard(Guid competitionId, bool frozen)
        {
            var generatedAt = DateTimeOffset.UtcNow.AddMinutes(frozen ? -5 : 0);
            return new ScoreboardProjection(
                new ScoreboardChallengeCatalog(competitionId, 1, []),
                new ScoreboardSchema(competitionId, GameMode.Ctf, 1, 1, [], []),
                new ScoreboardSnapshot(competitionId, 1, 1, generatedAt, null, [], [])
                {
                    Visibility = frozen
                        ? CompetitionLeaderboardVisibility.Frozen
                        : CompetitionLeaderboardVisibility.Normal,
                    DataScope = frozen ? LeaderboardDataScope.Frozen : LeaderboardDataScope.Live,
                    DataAsOf = generatedAt
                });
        }
    }

    private sealed class SwitchingLeaderboard(
        ScoreboardProjection first,
        ScoreboardProjection second) : ILeaderboardCache
    {
        private int reads;

        public Task<ScoreboardProjection?> GetFrozenScoreboardAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ScoreboardProjection?>(Interlocked.Increment(ref reads) == 1
                ? first
                : second);

        public Task<LeaderboardResponse?> GetFrozenAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult<LeaderboardResponse?>(null);

        public Task<ScoreboardProjection?> GetScoreboardAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult<ScoreboardProjection?>(null);

        public Task<LeaderboardResponse?> GetAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult<LeaderboardResponse?>(null);

        public Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InternalTrackStore(Guid competitionId) : ICompetitionTrackStore
    {
        public Task<CompetitionTracksView?> GetAsync(
            Guid requestedCompetitionId,
            Guid? viewerUserId,
            bool includeInternal,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionTracksView?>(requestedCompetitionId == competitionId
                ? new CompetitionTracksView(
                    competitionId,
                    GameMode.Ctf,
                    CompetitionStatus.Running,
                    1,
                    false,
                    [
                        new("default", "Default", true, true, false, true, true, true, true, true),
                        new("staff", "Staff", false, false, true, false, false, false, false, false)
                    ])
                : null);

        public Task<NoCTF.Application.Common.OperationResult<CompetitionTracksView, CompetitionTrackFailureCode>> UpdateAsync(
            UpdateCompetitionTracksCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NoCTF.Application.Common.OperationResult<TeamTrackAssignmentView, CompetitionTrackFailureCode>> AssignAsync(
            AssignTeamTrackCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingSlotDetailReader(
        IReadOnlyList<ScoreboardSlotDetailFact> facts) : IScoreboardSlotDetailReader
    {
        public List<ScoreboardSlotDetailQuery> Queries { get; } = [];

        public Task<IReadOnlyList<ScoreboardSlotDetailFact>> ReadAsync(
            ScoreboardSlotDetailQuery query,
            CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult(facts);
        }
    }

    private static ScoreboardProjection CreateProjection(
        Guid competitionId,
        IReadOnlyList<ScoreboardChallengeCatalogItem> challenges,
        IReadOnlyList<ScoreboardColumn> columns,
        IReadOnlyList<ScoreboardTeam> teams,
        IReadOnlyList<ScoreboardActor>? actors = null) => new(
        new ScoreboardChallengeCatalog(competitionId, 9, challenges),
        new ScoreboardSchema(competitionId, GameMode.Ctf, 11, 9, [], columns),
        new ScoreboardSnapshot(competitionId, 17, 11, DateTimeOffset.UtcNow, null, actors ?? [], teams));

    private sealed class RecordingMessagePublisher : IBackendMessagePublisher
    {
        public List<Guid> ProjectedCompetitionIds { get; } = [];

        public ValueTask ProjectLeaderboardAsync(
            Guid competitionId,
            CancellationToken cancellationToken)
        {
            ProjectedCompetitionIds.Add(competitionId);
            return ValueTask.CompletedTask;
        }

        public ValueTask RebuildCompetitionAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask CleanupCompetitionRuntimesAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask ProvisionCompetitionRuntimesAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

}
