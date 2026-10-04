using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using FastEndpoints.OpenApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.CheatIncidents;
using NoCTF.API.Endpoints.Administration.Teams;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.CheatIncidents;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.API;

[NotInParallel]
public sealed class CheatIncidentEndpointTests
{
    private static readonly Guid ActorId =
        Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7286");
    private static readonly Guid CompetitionId =
        Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7287");
    private static readonly Guid GameplayFactId =
        Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7288");
    private static readonly Guid TeamId =
        Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7289");
    private const string SigningKey =
        "cheat-incident-tests-use-a-stable-32-byte-signing-key";

    [Test]
    public async Task Observer_can_list_redacted_incidents_but_cannot_access_full_evidence()
    {
        var authorizer = new TestAuthorizer { Access = TestAccess.Observer };
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(store, authorizer);
        using var client = app.GetTestClient();

        using var listResponse = await client.GetAsync(ListUri());
        var page = await listResponse.Content.ReadFromJsonAsync<CheatIncidentListResponse>();
        using var detailResponse = await client.GetAsync(DetailUri());

        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(page).IsNotNull();
        await Assert.That(page!.Items).HasSingleItem();
        await Assert.That(page.Items[0].OwnerTeamId).IsNull();
        await Assert.That(page.Items[0].OwnerTeamName).IsNull();
        await Assert.That(page.Items[0].GetType().GetProperty("Value")).IsNull();
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(store.DetailReads).IsEqualTo(0);
    }

    [Test]
    public async Task Judge_evidence_is_no_store_and_both_adjudication_outcomes_are_allowed()
    {
        var authorizer = new TestAuthorizer { Access = TestAccess.Judge };
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(store, authorizer);
        using var client = app.GetTestClient();

        using var detailResponse = await client.GetAsync(DetailUri());
        var detail = await detailResponse.Content.ReadFromJsonAsync<CheatIncidentDetailResponse>();
        using var dismissResponse = await client.PutAsJsonAsync(
            $"{DetailUri()}/status",
            new { status = "Dismissed", reason = "reviewed evidence" });
        using var confirmResponse = await client.PutAsJsonAsync(
            $"{DetailUri()}/status",
            new { status = "Confirmed", reason = "confirmed evidence" });

        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(detailResponse.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(detailResponse.Headers.CacheControl?.NoCache).IsTrue();
        await Assert.That(detail).IsNotNull();
        await Assert.That(detail!.Value).IsEqualTo("flag{protected-evidence}");
        await Assert.That(detail.OwnerTeamId).IsNull();
        await Assert.That(detail.OwnerTeamName).IsNull();
        await Assert.That(detail.CanDismiss).IsTrue();
        await Assert.That(detail.CanConfirm).IsTrue();
        await Assert.That(store.LastDetailReadShouldAudit).IsTrue();
        await Assert.That(dismissResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(store.DismissCalls).IsEqualTo(1);
        await Assert.That(confirmResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(store.ConfirmCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Platform_administrator_evidence_read_is_not_audited()
    {
        var authorizer = new TestAuthorizer { Access = TestAccess.Moderator };
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(
            store,
            authorizer,
            isAdministrator: true);
        using var client = app.GetTestClient();

        using var detailResponse = await client.GetAsync(DetailUri());

        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(store.LastDetailReadShouldAudit).IsFalse();
    }

    [Test]
    public async Task Moderator_can_confirm_and_correct_with_valid_staff_reasons()
    {
        var authorizer = new TestAuthorizer { Access = TestAccess.Moderator };
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(store, authorizer);
        using var client = app.GetTestClient();

        using var confirmResponse = await client.PutAsJsonAsync(
            $"{DetailUri()}/status",
            new { status = "Confirmed", reason = "confirmed evidence" });
        using var correctResponse = await client.PutAsJsonAsync(
            $"{DetailUri()}/status",
            new { status = "Corrected", reason = "confirmed false positive" });

        await Assert.That(confirmResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(correctResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(store.ConfirmCalls).IsEqualTo(1);
        await Assert.That(store.CorrectCalls).IsEqualTo(1);
    }

    private static string ListUri()
    {
        var from = Uri.EscapeDataString("2026-08-01T00:00:00Z");
        var to = Uri.EscapeDataString("2026-08-02T00:00:00Z");
        return $"/api/v1/admin/competitions/{CompetitionId}/cheat-incidents"
            + $"?from={from}&to={to}&limit=50";
    }

    private static string DetailUri() =>
        $"/api/v1/admin/competitions/{CompetitionId}/cheat-incidents/{GameplayFactId}";

    private static async Task<WebApplication> CreateApplicationAsync(
        RecordingStore store,
        TestAuthorizer authorizer,
        bool isAdministrator = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Authentication:SigningKey"] = SigningKey;
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(ListCheatIncidentsEndpoint).Assembly];
            options.Filter = type => type.Namespace
                    == typeof(ListCheatIncidentsEndpoint).Namespace;
        });
        builder.Services.OpenApiDocument();
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Bearer";
                options.DefaultChallengeScheme = "Bearer";
            })
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>(
                "Bearer",
                _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<ICheatIncidentStore>(store);
        builder.Services.AddSingleton<ITeamModerationStore>(store);
        builder.Services.AddScoped<ListCheatIncidents>();
        builder.Services.AddScoped<AccessCheatIncident>();
        builder.Services.AddScoped<ResolveCheatIncident>();
        builder.Services.AddScoped<ModerateTeam>();
        builder.Services.AddSingleton<ICompetitionModerationAuthorizer>(authorizer);
        builder.Services.AddSingleton<SignedKeysetCursor>();
        builder.Services.AddSingleton<IUserContext>(new ActorUserContext(isAdministrator));
        builder.Services.AddSingleton(TimeProvider.System);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class RecordingStore : ICheatIncidentStore, ITeamModerationStore
    {
        private static readonly DateTimeOffset DetectedAt =
            DateTimeOffset.Parse("2026-08-01T12:00:00Z");

        public int DetailReads { get; private set; }
        public bool? LastDetailReadShouldAudit { get; private set; }
        public int DismissCalls { get; private set; }
        public int ConfirmCalls { get; private set; }
        public int CorrectCalls { get; private set; }
        public int TeamModerationCalls { get; private set; }
        public TeamModerationCommand? LastTeamModeration { get; private set; }

        public Task<CheatIncidentPage?> ListAsync(
            CheatIncidentQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult<CheatIncidentPage?>(new([
                new(
                    GameplayFactId,
                    Guid.CreateVersion7(),
                    "Source Team",
                    null,
                    null,
                    ActorId,
                    "submitter",
                    Guid.CreateVersion7(),
                    "Challenge",
                    GameplayFactKind.FlagAttempt,
                    GameplayFactResult.Rejected,
                    GameplayFactFailureCode.ForeignTeamFlagDetected,
                    CheatIncidentStatus.Pending,
                    null,
                    null,
                    null,
                    null,
                    DetectedAt,
                    DetectedAt,
                    false)
            ], 1));

        public Task<CheatIncidentDetail?> GetDetailAsync(
            CheatIncidentAccessCommand command,
            CancellationToken cancellationToken)
        {
            DetailReads++;
            LastDetailReadShouldAudit = command.ShouldAuditAccess;
            return Task.FromResult<CheatIncidentDetail?>(new(
                GameplayFactId,
                "flag{protected-evidence}",
                Guid.CreateVersion7(),
                "Source Team",
                null,
                null,
                ActorId,
                "submitter",
                Guid.CreateVersion7(),
                "Challenge",
                GameplayFactKind.FlagAttempt,
                GameplayFactResult.Rejected,
                GameplayFactFailureCode.ForeignTeamFlagDetected,
                CheatIncidentStatus.Pending,
                null,
                null,
                null,
                null,
                DetectedAt,
                DetectedAt,
                false,
                null,
                null,
                null));
        }

        public Task<CheatIncidentResolutionResult> DismissAsync(
            CheatIncidentResolutionCommand command,
            CancellationToken cancellationToken)
        {
            DismissCalls++;
            return Task.FromResult(new CheatIncidentResolutionResult());
        }

        public Task<CheatIncidentResolutionResult> ConfirmAndBanAsync(
            CheatIncidentResolutionCommand command,
            CancellationToken cancellationToken)
        {
            ConfirmCalls++;
            return Task.FromResult(new CheatIncidentResolutionResult());
        }

        public Task<CheatIncidentResolutionResult> CorrectAndUnbanAsync(
            CheatIncidentResolutionCommand command,
            CancellationToken cancellationToken)
        {
            CorrectCalls++;
            return Task.FromResult(new CheatIncidentResolutionResult());
        }

        public Task<CompetitionStatus?> GetCompetitionStatusAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionStatus?>(CompetitionStatus.Running);

        public Task<TeamModerationStoreResult> ApplyAsync(
            TeamModerationCommand command,
            CancellationToken cancellationToken)
        {
            TeamModerationCalls++;
            LastTeamModeration = command;
            return Task.FromResult(new TeamModerationStoreResult());
        }
    }

    private sealed class TestAuthorizer : ICompetitionModerationAuthorizer
    {
        public TestAccess Access { get; init; }

        public Task<bool> CanModerateAsync(
            Guid userId,
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Access == TestAccess.Moderator);

        public Task<bool> CanJudgeAsync(
            Guid userId,
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Access is TestAccess.Judge or TestAccess.Moderator);

        public Task<bool> CanObserveAsync(
            Guid userId,
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private enum TestAccess
    {
        Observer,
        Judge,
        Moderator
    }

    private sealed class ActorUserContext(bool isAdministrator) : IUserContext
    {
        public Guid UserId => ActorId;
        public bool IsAdministrator => isAdministrator;
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
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
