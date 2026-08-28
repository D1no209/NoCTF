using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.GameplayFacts;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.Tests.Unit.API;

[NotInParallel]
public sealed class ManualAdjustmentEndpointTests
{
    private static readonly Guid ActorId = Guid.Parse("01a04d15-d3b7-754b-b399-8106afe5be01");
    private static readonly Guid CompetitionId = Guid.Parse("01a04d15-d3b7-754b-b399-8106afe5be02");
    private static readonly Guid TeamId = Guid.Parse("01a04d15-d3b7-754b-b399-8106afe5be03");
    private static readonly Guid ChallengeId = Guid.Parse("01a04d15-d3b7-754b-b399-8106afe5be04");
    private const string SigningKey = "manual-adjustment-tests-use-a-stable-signing-key";

    [Test]
    public async Task Judge_can_record_a_challenge_score_adjustment()
    {
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(store, canJudge: true);
        using var response = await app.GetTestClient().PostAsJsonAsync(
            EndpointUri(),
            new { teamId = TeamId, competitionChallengeId = ChallengeId, delta = -25 });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(store.Received).IsNotNull();
        await Assert.That(store.Received!.TeamId).IsEqualTo(TeamId);
        await Assert.That(store.Received.CompetitionChallengeId).IsEqualTo(ChallengeId);
        await Assert.That(store.Received.Delta).IsEqualTo(-25);
        await Assert.That(store.Received.UserId).IsEqualTo(ActorId);
    }

    [Test]
    public async Task Observer_cannot_record_a_challenge_score_adjustment()
    {
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(store, canJudge: false);
        using var response = await app.GetTestClient().PostAsJsonAsync(
            EndpointUri(),
            new { teamId = TeamId, competitionChallengeId = ChallengeId, delta = 10 });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(store.Received).IsNull();
    }

    private static string EndpointUri() =>
        $"/api/v1/admin/competitions/{CompetitionId}/gameplay-facts/manual-adjustments";

    private static async Task<WebApplication> CreateApplicationAsync(
        RecordingStore store,
        bool canJudge)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Authentication:SigningKey"] = SigningKey;
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(CreateManualAdjustmentEndpoint).Assembly];
            options.Filter = type => type == typeof(CreateManualAdjustmentEndpoint);
        });
        builder.Services.SwaggerDocument();
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Bearer";
                options.DefaultChallengeScheme = "Bearer";
            })
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IGameplayFactIntakeStore>(store);
        builder.Services.AddScoped<CreateManualAdjustment>();
        builder.Services.AddSingleton<ICompetitionModerationAuthorizer>(new TestAuthorizer(canJudge));
        builder.Services.AddSingleton<IUserContext>(new ActorUserContext());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class RecordingStore : IGameplayFactIntakeStore
    {
        public ManualAdjustmentGameplayFactReceived? Received { get; private set; }

        public Task<GameplayFactAdmissionSnapshot?> LoadAdmissionAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid userId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GameplayFactAcceptanceResult> TryAcceptFlagAsync(
            FlagGameplayFactReceived received,
            GameplayFactAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GameplayFactAcceptanceResult>> TryAcceptFlagsAsync(
            IReadOnlyList<FlagGameplayFactReceived> received,
            GameplayFactAdmissionSnapshot snapshot,
            int? maxAttempts,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GameplayFactAcceptanceResult> TryAcceptManualAdjustmentAsync(
            ManualAdjustmentGameplayFactReceived received,
            CancellationToken cancellationToken)
        {
            Received = received;
            return Task.FromResult(new GameplayFactAcceptanceResult(
                GameplayFactAcceptanceState.Created,
                received.GameplayFactId,
                received.OccurredAt));
        }
    }

    private sealed class TestAuthorizer(bool canJudge) : ICompetitionModerationAuthorizer
    {
        public Task<bool> CanModerateAsync(
            Guid userId,
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> CanJudgeAsync(
            Guid userId,
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult(canJudge);

        public Task<bool> CanObserveAsync(
            Guid userId,
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult(canJudge);
    }

    private sealed class ActorUserContext : IUserContext
    {
        public Guid UserId => ActorId;
        public bool IsAdministrator => false;
    }

    private sealed class TestBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, ActorId.ToString())],
                Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
