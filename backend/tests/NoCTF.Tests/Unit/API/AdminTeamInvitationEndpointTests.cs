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
using NoCTF.API.Endpoints.Administration.Teams;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.Tests.Unit.API;

[NotInParallel]
public sealed class AdminTeamInvitationEndpointTests
{
    private static readonly Guid ActorId = Guid.CreateVersion7();
    private static readonly Guid CompetitionId = Guid.CreateVersion7();
    private static readonly Guid TeamId = Guid.CreateVersion7();
    private const string Token = "0123456789ABCDEFGHIJKLMNOPQRSTUV";

    [Test]
    public async Task Manager_can_read_the_current_invitation_without_caching_it()
    {
        var reader = new RecordingReader(Token);
        await using var app = await CreateApplicationAsync(reader, canModerate: true);

        using var response = await app.GetTestClient().GetAsync(RequestUri());
        var invitation = await response.Content
            .ReadFromJsonAsync<AdminTeamInvitationResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl!.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl.NoStore).IsTrue();
        await Assert.That(invitation!.InvitationToken).IsEqualTo(Token);
        await Assert.That(reader.CompetitionId).IsEqualTo(CompetitionId);
        await Assert.That(reader.TeamId).IsEqualTo(TeamId);
    }

    [Test]
    public async Task Observer_cannot_read_the_invitation()
    {
        var reader = new RecordingReader(Token);
        await using var app = await CreateApplicationAsync(reader, canModerate: false);

        using var response = await app.GetTestClient().GetAsync(RequestUri());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(reader.CompetitionId).IsNull();
    }

    [Test]
    public async Task Missing_team_returns_not_found()
    {
        var reader = new RecordingReader(null);
        await using var app = await CreateApplicationAsync(reader, canModerate: true);

        using var response = await app.GetTestClient().GetAsync(RequestUri());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    private static string RequestUri() =>
        $"/api/v1/admin/competitions/{CompetitionId}/teams/{TeamId}/invitation-token";

    private static async Task<WebApplication> CreateApplicationAsync(
        RecordingReader reader,
        bool canModerate)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(GetAdminTeamInvitationEndpoint).Assembly];
            options.Filter = type => type == typeof(GetAdminTeamInvitationEndpoint);
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
        builder.Services.AddSingleton<IAdminTeamInvitationReader>(reader);
        builder.Services.AddScoped<GetAdminTeamInvitation>();
        builder.Services.AddSingleton<ICompetitionModerationAuthorizer>(
            new TestAuthorizer(canModerate));
        builder.Services.AddSingleton<IUserContext>(new TestUserContext());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class RecordingReader(string? token) : IAdminTeamInvitationReader
    {
        public Guid? CompetitionId { get; private set; }
        public Guid? TeamId { get; private set; }

        public Task<string?> ReadAsync(
            Guid competitionId,
            Guid teamId,
            CancellationToken cancellationToken)
        {
            CompetitionId = competitionId;
            TeamId = teamId;
            return Task.FromResult(token);
        }
    }

    private sealed class TestAuthorizer(bool canModerate)
        : ICompetitionModerationAuthorizer
    {
        public Task<bool> CanModerateAsync(
            Guid userId,
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult(canModerate);
    }

    private sealed class TestUserContext : IUserContext
    {
        public Guid UserId => ActorId;
        public bool IsAdministrator => true;
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
