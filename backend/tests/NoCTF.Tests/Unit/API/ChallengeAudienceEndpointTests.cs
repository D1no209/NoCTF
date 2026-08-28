using System.Net;
using System.Net.Http.Json;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class ChallengeAudienceEndpointTests
{
    private static readonly Guid CompetitionId = Guid.CreateVersion7();
    private static readonly Guid ChallengeId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();

    [Test]
    public async Task Ineligible_actor_cannot_list_or_get_published_challenges()
    {
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(store, canRead: false);
        using var client = app.GetTestClient();

        using var list = await client.GetAsync($"/api/v1/competitions/{CompetitionId}/challenges");
        using var detail = await client.GetAsync($"/api/v1/competitions/{CompetitionId}/challenges/{ChallengeId}");

        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(store.ListCalls).IsEqualTo(0);
        await Assert.That(store.FindCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Eligible_actor_can_list_and_get_published_challenges()
    {
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(store, canRead: true);
        using var client = app.GetTestClient();

        using var list = await client.GetAsync($"/api/v1/competitions/{CompetitionId}/challenges");
        using var detail = await client.GetAsync($"/api/v1/competitions/{CompetitionId}/challenges/{ChallengeId}");

        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var response = await detail.Content.ReadFromJsonAsync<ChallengeResponse>();
        await Assert.That(response?.UsesDynamicFlag).IsTrue();
        await Assert.That(store.ListCalls).IsEqualTo(1);
        await Assert.That(store.FindCalls).IsEqualTo(1);
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        RecordingStore store,
        bool canRead)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(ListChallengesEndpoint).Assembly];
            options.Filter = type => type == typeof(ListChallengesEndpoint)
                || type == typeof(GetChallengeEndpoint);
        });
        builder.Services.AddSingleton<IUserContext>(new ActorUserContext());
        builder.Services.AddSingleton<ICompetitionChallengeAudienceAccess>(
            new FixedAudienceAccess(canRead));
        builder.Services.AddSingleton<ICompetitionVisibilityAccess>(new RunningVisibilityAccess());
        builder.Services.AddSingleton<IKohChallengeAccessReader>(new EmptyKohAccess());
        builder.Services.AddSingleton<IChallengeManagementStore>(store);
        builder.Services.AddSingleton(Substitute.For<IGameplayFactIntakeStore>());
        builder.Services.AddSingleton(Substitute.For<IGameplayFactAdmissionModePolicy>());
        builder.Services.AddScoped<ListChallenges>();
        builder.Services.AddScoped<GetChallenge>();
        builder.Services.AddScoped<GetFlagAttemptBudget>();

        var app = builder.Build();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class FixedAudienceAccess(bool canRead) : ICompetitionChallengeAudienceAccess
    {
        public Task<bool> CanReadAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(canRead);
    }

    private sealed class RunningVisibilityAccess : ICompetitionVisibilityAccess
    {
        public Task<CompetitionVisibilityAccessDecision?> ResolveAsync(
            Guid userId,
            Guid competitionId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionVisibilityAccessDecision?>(new(
                GameMode.Ctf,
                CompetitionStatus.Running,
                CompetitionLeaderboardVisibility.Normal,
                LeaderboardDataScope.Live));
    }

    private sealed class EmptyKohAccess : IKohChallengeAccessReader
    {
        public Task<KohChallengeAccessView?> FindAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<KohChallengeAccessView?>(null);
    }

    private sealed class RecordingStore : IChallengeManagementStore
    {
        private readonly ChallengeView challenge = new(
            ChallengeId,
            CompetitionId,
            Guid.CreateVersion7(),
            "Visible challenge",
            null,
            "statement",
            "PWN",
            1,
            true,
            null,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow)
        {
            UsesDynamicFlag = true
        };

        public int ListCalls { get; private set; }
        public int FindCalls { get; private set; }

        public Task<ChallengeView?> FindAsync(Guid competitionId, Guid competitionChallengeId,
            bool includeUnpublished, bool includeDeleted, CancellationToken cancellationToken)
        {
            FindCalls++;
            return Task.FromResult<ChallengeView?>(challenge);
        }

        public Task<IReadOnlyList<ChallengeView>> ListAsync(Guid competitionId,
            bool includeUnpublished, bool includeDeleted, CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<ChallengeView>>([challenge]);
        }

        public Task<ChallengeCompetitionContext?> GetCompetitionAsync(Guid competitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChallengeMutationResult> CreateAsync(CreateCompetitionChallengeCommand command, string configurationJson, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChallengeMutationResult> UpdateAsync(UpdateCompetitionChallengeCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChallengeMutationFailure?> SoftDeleteAsync(Guid competitionId, Guid competitionChallengeId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChallengeMutationFailure?> RestoreAsync(Guid competitionId, Guid competitionChallengeId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ActorUserContext : IUserContext
    {
        public Guid UserId => ChallengeAudienceEndpointTests.UserId;
        public bool IsAdministrator => false;
    }
}
