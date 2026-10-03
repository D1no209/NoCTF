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
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Domain.Challenges;
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
    private static readonly Guid TeamId = Guid.CreateVersion7();

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
        await Assert.That(response!.Tags.SequenceEqual(["Web", "SQL"])).IsTrue();
        var listed = await list.Content.ReadFromJsonAsync<ChallengeListResponse>();
        await Assert.That(listed!.Items.Single().Tags.SequenceEqual(response.Tags)).IsTrue();
        await Assert.That(response?.Hints?.Count).IsEqualTo(2);
        await Assert.That(response!.Hints!.Single(hint => hint.Cost == 0).Content).IsEqualTo("Public hint");
        await Assert.That(response.Hints!.Single(hint => hint.Cost == 20).Content).IsNull();
        await Assert.That(detail.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(detail.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(store.ListCalls).IsEqualTo(1);
        await Assert.That(store.FindCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Detail_reuses_authorized_team_for_attempt_state()
    {
        var store = new RecordingStore();
        var reader = Substitute.For<IFlagAttemptStateReader>();
        reader.ReadAsync(CompetitionId, ChallengeId, TeamId, GameMode.Ctf,
                CompetitionStatus.Running, Arg.Any<CancellationToken>())
            .Returns(new FlagAttemptState(3, 1, 2, false));
        await using var app = await CreateApplicationAsync(store, canRead: true,
            teamId: TeamId, attemptReader: reader);
        using var client = app.GetTestClient();

        using var detail = await client.GetAsync(
            $"/api/v1/competitions/{CompetitionId}/challenges/{ChallengeId}");
        var response = await detail.Content.ReadFromJsonAsync<ChallengeResponse>();

        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response?.RemainingFlagAttempts).IsEqualTo(2);
        await reader.Received(1).ReadAsync(CompetitionId, ChallengeId, TeamId,
            GameMode.Ctf, CompetitionStatus.Running, Arg.Any<CancellationToken>());
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        RecordingStore store,
        bool canRead,
        Guid? teamId = null,
        IFlagAttemptStateReader? attemptReader = null)
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
        builder.Services.AddSingleton<ICompetitionChallengeReadAccess>(
            new FixedReadAccess(canRead, teamId));
        builder.Services.AddSingleton<IKohChallengeAccessReader>(new UnexpectedKohAccess());
        builder.Services.AddSingleton<IChallengeManagementStore>(store);
        builder.Services.AddSingleton(attemptReader ?? Substitute.For<IFlagAttemptStateReader>());
        var progression = Substitute.For<IProgressionChallengeAccess>();
        progression.IsActiveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>()).Returns(true);
        progression.ReadStatusesAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>()).Returns(
                new Dictionary<Guid, ProgressionChallengeStatus>());
        builder.Services.AddSingleton(progression);
        builder.Services.AddScoped<ListChallenges>();
        builder.Services.AddScoped<GetChallenge>();
        builder.Services.AddScoped<GetFlagAttemptState>();
        var hintStore = Substitute.For<IParticipantChallengeHintStore>();
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        hintStore.ReadAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CompetitionStatus>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_ => canRead
                ? new ParticipantChallengeHintAccess(
                    [
                        new(Guid.NewGuid(), ChallengeId, "Public hint", 0, now, null, now, now),
                        new(Guid.NewGuid(), ChallengeId, "Locked hint", 20, now, null, now, now),
                        new(Guid.NewGuid(), ChallengeId, "Unpublished hint", 0, null, null, now, now)
                    ], new HashSet<Guid>(), true)
                : throw new InvalidOperationException("Hint data must not be read before audience authorization."));
        builder.Services.AddSingleton(hintStore);
        builder.Services.AddScoped<ReadParticipantChallengeHints>();

        var app = builder.Build();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class FixedReadAccess(bool canRead, Guid? teamId) : ICompetitionChallengeReadAccess
    {
        public Task<CompetitionChallengeReadDecision?> ResolveAsync(
            Guid userId, Guid competitionId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionChallengeReadDecision?>(canRead
                ? new(new(GameMode.Ctf, CompetitionStatus.Running,
                    CompetitionLeaderboardVisibility.Normal, LeaderboardDataScope.Live), teamId)
                : null);
    }

    private sealed class UnexpectedKohAccess : IKohChallengeAccessReader
    {
        public Task<KohChallengeAccessView?> FindAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid userId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Non-KoH challenge must not load KoH access.");
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
            Tags = ["Web", "SQL"],
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

        public Task<IReadOnlyList<CompetitionChallengeSummaryView>> ListAsync(Guid competitionId,
            bool includeUnpublished, bool includeDeleted, CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<CompetitionChallengeSummaryView>>([
                new(challenge.Id, challenge.CompetitionId, challenge.ChallengeId,
                    challenge.Title, challenge.CustomTitle, challenge.Direction,
                    challenge.Order, challenge.IsPublished, challenge.DeletedAt,
                    challenge.InteractionKind) { Tags = challenge.Tags }
            ]);
        }

        public Task<ChallengeCompetitionContext?> GetCompetitionAsync(Guid competitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChallengeMutationResult> CreateAsync(CreateCompetitionChallengeCommand command, CompetitionChallengeRules rules, CancellationToken cancellationToken) => throw new NotSupportedException();
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
