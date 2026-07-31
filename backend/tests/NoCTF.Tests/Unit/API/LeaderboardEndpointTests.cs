using System.Net;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.API;

public sealed class LeaderboardEndpointTests
{
    [Test]
    [Arguments(true, 1)]
    [Arguments(false, 0)]
    public async Task Cached_snapshot_only_queues_refresh_when_stale(
        bool stale,
        int expectedMessages)
    {
        var competitionId = Guid.CreateVersion7();
        var messages = new RecordingMessagePublisher();
        await using var app = await CreateApplicationAsync(
            competitionId,
            new CachedLeaderboard(stale),
            messages);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            $"/api/v1/competitions/{competitionId}/leaderboard");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(messages.ProjectedCompetitionIds).Count()
            .IsEqualTo(expectedMessages);
        if (stale)
        {
            await Assert.That(messages.ProjectedCompetitionIds[0])
                .IsEqualTo(competitionId);
        }
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        Guid competitionId,
        ILeaderboardCache leaderboard,
        IBackendMessagePublisher messages)
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
        builder.Services.AddSingleton<ICompetitionManagementStore>(
            new CompetitionStore(competitionId));
        builder.Services.AddScoped<GetCompetition>();

        var app = builder.Build();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class CachedLeaderboard(bool stale) : ILeaderboardCache
    {
        public Task<LeaderboardResponse?> GetAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LeaderboardResponse?>(new(
                competitionId,
                DateTimeOffset.UtcNow,
                [])
            {
                SnapshotRevision = stale ? 1 : 2,
                TargetRevision = 2,
                Stale = stale
            });

        public Task RefreshAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task InvalidateAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
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

    private sealed class CompetitionStore(Guid competitionId)
        : ICompetitionManagementStore
    {
        public Task<CompetitionView?> FindAsync(
            Guid requestedCompetitionId,
            bool includeDraft,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionView?>(
                requestedCompetitionId == competitionId
                    ? new(
                        competitionId,
                        "Leaderboard",
                        null,
                        GameMode.Ctf,
                        DateTimeOffset.UtcNow.AddHours(-1),
                        DateTimeOffset.UtcNow.AddHours(1),
                        CompetitionStatus.Running,
                        true,
                        5,
                        0,
                        Guid.CreateVersion7())
                    : null);

        public Task<CompetitionCreationResult> CreateAsync(
            CreateCompetitionCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CompetitionView>> ListAsync(
            bool includeDraft,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CompetitionView?> UpdateAsync(
            UpdateCompetitionCommand command,
            CompetitionStatus expectedStatus,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SoftDeleteAsync(
            Guid requestedCompetitionId,
            CompetitionStatus expectedStatus,
            Guid actorId,
            DateTimeOffset deletedAt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
