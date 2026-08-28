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
using NoCTF.API.Endpoints.Administration.Teams;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Appeals;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Teams;

namespace NoCTF.Tests.Unit.API;

[NotInParallel]
public sealed class TeamBanAppealEndpointTests
{
    private static readonly Guid ActorId =
        Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7290");
    private static readonly Guid CompetitionId =
        Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7291");
    private static readonly Guid AppealId =
        Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7292");
    private static readonly Guid TeamId =
        Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7293");
    private const string SigningKey =
        "team-ban-appeal-tests-use-a-stable-signing-key";

    [Test]
    public async Task Judge_can_resolve_submitted_appeals()
    {
        var authorizer = new TestAuthorizer { Access = TestAccess.Judge };
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(store, authorizer);
        using var client = app.GetTestClient();

        using var listResponse = await client.GetAsync(ListUri());
        var page = await listResponse.Content
            .ReadFromJsonAsync<AdminTeamBanAppealListResponse>();
        using var acceptResponse = await client.PostAsJsonAsync(
            $"{ListUri()}/{AppealId}/accept",
            new { reason = "appeal evidence accepted" });
        using var upholdResponse = await client.PostAsJsonAsync(
            $"{ListUri()}/{AppealId}/uphold",
            new { reason = "appeal evidence rejected" });

        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(page).IsNotNull();
        await Assert.That(page!.Items).HasSingleItem();
        await Assert.That(page.Items[0].CanResolve).IsTrue();
        await Assert.That(acceptResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(upholdResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(store.Resolutions).Count().IsEqualTo(2);
        await Assert.That(store.Resolutions[0].Resolution)
            .IsEqualTo(TeamBanAppealResolution.Accept);
        await Assert.That(store.Resolutions[1].Resolution)
            .IsEqualTo(TeamBanAppealResolution.Uphold);
        await Assert.That(store.Resolutions.All(command => command.ActorUserId == ActorId))
            .IsTrue();
    }

    [Test]
    public async Task Observer_can_read_appeals_but_cannot_resolve_them()
    {
        var authorizer = new TestAuthorizer { Access = TestAccess.Observer };
        var store = new RecordingStore();
        await using var app = await CreateApplicationAsync(store, authorizer);
        using var client = app.GetTestClient();

        using var listResponse = await client.GetAsync(ListUri());
        var page = await listResponse.Content
            .ReadFromJsonAsync<AdminTeamBanAppealListResponse>();
        using var resolveResponse = await client.PostAsJsonAsync(
            $"{ListUri()}/{AppealId}/accept",
            new { reason = "observer cannot resolve this appeal" });

        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(page).IsNotNull();
        await Assert.That(page!.Items).HasSingleItem();
        await Assert.That(page.Items[0].CanResolve).IsFalse();
        await Assert.That(resolveResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(store.Resolutions).IsEmpty();
    }

    private static string ListUri() =>
        $"/api/v1/admin/competitions/{CompetitionId}/team-ban-appeals";

    private static async Task<WebApplication> CreateApplicationAsync(
        RecordingStore store,
        TestAuthorizer authorizer)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Authentication:SigningKey"] = SigningKey;
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(ListTeamBanAppealsEndpoint).Assembly];
            options.Filter = type => type == typeof(ListTeamBanAppealsEndpoint)
                || type == typeof(AcceptTeamBanAppealEndpoint)
                || type == typeof(UpholdTeamBanAppealEndpoint);
        });
        builder.Services.SwaggerDocument();
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
        builder.Services.AddSingleton<ITeamBanAppealStore>(store);
        builder.Services.AddScoped<ListTeamBanAppeals>();
        builder.Services.AddScoped<ResolveTeamBanAppeal>();
        builder.Services.AddScoped<CorrectTeamBan>();
        builder.Services.AddSingleton<ICompetitionModerationAuthorizer>(authorizer);
        builder.Services.AddSingleton<IUserContext>(new ActorUserContext());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class RecordingStore : ITeamBanAppealStore
    {
        private static readonly DateTimeOffset SubmittedAt =
            DateTimeOffset.Parse("2026-08-10T04:00:00Z");

        public List<ResolveTeamBanAppealCommand> Resolutions { get; } = [];

        public Task<TeamBanCaseView?> GetForMemberAsync(
            Guid competitionId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<TeamBanCaseView?>(Case());

        public Task<IReadOnlyList<TeamBanCaseView>?> ListForStaffAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TeamBanCaseView>?>([Case()]);

        public Task<TeamBanAppealMutationResult> SubmitAsync(
            SubmitTeamBanAppealCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TeamBanAppealMutationResult(Case()));

        public Task<TeamBanAppealMutationResult> ResolveAsync(
            ResolveTeamBanAppealCommand command,
            CancellationToken cancellationToken)
        {
            Resolutions.Add(command);
            return Task.FromResult(new TeamBanAppealMutationResult(Case()));
        }

        public Task<TeamBanAppealMutationResult> CorrectAsync(
            CorrectTeamBanCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TeamBanAppealMutationResult(Case()));

        private static TeamBanCaseView Case() => new(
            Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7294"),
            CompetitionId,
            TeamId,
            "Appealing Team",
            TeamBanSource.ManualModeration,
            SubmittedAt.AddMinutes(-10),
            true,
            false,
            new TeamBanAppealView(
                AppealId,
                Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7295"),
                "captain",
                "The team contests this ban with evidence.",
                SubmittedAt,
                TeamBanAppealStatus.Submitted,
                null,
                null,
                null,
                null));
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
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
