using System.Net;
using System.Net.Http.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
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
        var body = await response.Content.ReadFromJsonAsync<LeaderboardProtocolResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Visibility)
            .IsEqualTo(LeaderboardVisibilityProtocol.Blackout);
        await Assert.That(body.DataScope).IsEqualTo(LeaderboardDataScopeProtocol.Hidden);
        await Assert.That(body.Entries).IsEmpty();
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
        var body = await response.Content.ReadFromJsonAsync<LeaderboardProtocolResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Visibility)
            .IsEqualTo(LeaderboardVisibilityProtocol.Frozen);
        await Assert.That(body.DataScope).IsEqualTo(LeaderboardDataScopeProtocol.Frozen);
        await Assert.That(messages.ProjectedCompetitionIds).IsEmpty();
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        Guid competitionId,
        ILeaderboardCache leaderboard,
        IBackendMessagePublisher messages,
        CompetitionLeaderboardVisibility visibility = CompetitionLeaderboardVisibility.Normal,
        LeaderboardDataScope dataScope = LeaderboardDataScope.Live)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(GetLeaderboardEndpoint).Assembly];
            options.Filter = type => type == typeof(GetLeaderboardEndpoint);
        });
        builder.Services.SwaggerDocument();
        builder.Services.AddSingleton(leaderboard);
        builder.Services.AddSingleton(messages);
        builder.Services.AddSingleton<ICompetitionVisibilityAccess>(
            new PublicVisibilityAccess(competitionId, visibility, dataScope));
        builder.Services.AddSingleton<GetCompetitionTracks>();
        builder.Services.AddSingleton<ICompetitionTrackStore>(new DefaultTrackStore(competitionId));
        builder.Services.AddSingleton<ICompetitionModerationAuthorizer>(new NoStaffAccess());
        builder.Services.AddSingleton<IUserContext>(new AnonymousUserContext());

        var app = builder.Build();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class PublicVisibilityAccess(
        Guid competitionId,
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
        DateTimeOffset? lastFailureAt = null) : ILeaderboardCache
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
    }

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
