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
using NoCTF.API.Endpoints.Teams;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Membership;

namespace NoCTF.Tests.Unit.API;

[NotInParallel]
public sealed class JoinTeamByInvitationEndpointTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid CompetitionId = Guid.CreateVersion7();
    private const string Token = "0123456789ABCDEFGHIJKLMNOPQRSTUV";

    [Test]
    public async Task Valid_invitation_returns_no_content()
    {
        var store = new RecordingMembershipStore();
        await using var app = await CreateApplicationAsync(store);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/v1/competitions/{CompetitionId}/teams/join",
            new { invitationToken = Token });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(store.InvitationToken).IsEqualTo(Token);
        await Assert.That(store.UserId).IsEqualTo(UserId);
    }

    [Test]
    public async Task Membership_failure_returns_a_typed_conflict()
    {
        var store = new RecordingMembershipStore(TeamMembershipFailure.TeamFull);
        await using var app = await CreateApplicationAsync(store);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/v1/competitions/{CompetitionId}/teams/join",
            new { invitationToken = Token });
        var failure = await response.Content.ReadFromJsonAsync<TeamMembershipFailureResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(failure!.Code).IsEqualTo(TeamMembershipFailureCodeProtocol.TeamFull);
    }

    [Test]
    public async Task Invalid_token_length_is_rejected_before_the_store()
    {
        var store = new RecordingMembershipStore();
        await using var app = await CreateApplicationAsync(store);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/v1/competitions/{CompetitionId}/teams/join",
            new { invitationToken = "short" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(store.InvitationToken).IsNull();
    }

    [Test]
    public async Task Captain_can_read_the_current_invitation_without_rotating_it()
    {
        var store = new RecordingMembershipStore(currentToken: Token);
        await using var app = await CreateApplicationAsync(store);

        using var response = await app.GetTestClient().GetAsync(
            $"/api/v1/competitions/{CompetitionId}/teams/{UserId}/invitation-token");
        var invitation = await response.Content
            .ReadFromJsonAsync<GetTeamInvitationResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl!.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl.NoStore).IsTrue();
        await Assert.That(invitation!.InvitationToken).IsEqualTo(Token);
    }

    [Test]
    public async Task Invitation_read_does_not_disclose_the_token_when_forbidden()
    {
        var store = new RecordingMembershipStore(
            currentToken: Token,
            readFailure: TeamMembershipFailure.TeamForbidden);
        await using var app = await CreateApplicationAsync(store);

        using var response = await app.GetTestClient().GetAsync(
            $"/api/v1/competitions/{CompetitionId}/teams/{UserId}/invitation-token");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        RecordingMembershipStore store)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(JoinTeamByInvitationEndpoint).Assembly];
            options.Filter = type => type == typeof(JoinTeamByInvitationEndpoint)
                || type == typeof(GetTeamInvitationEndpoint)
                || type == typeof(JoinTeamByInvitationValidator);
        });
        builder.Services.OpenApiDocument();
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Bearer";
                options.DefaultChallengeScheme = "Bearer";
            })
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<ITeamMembershipStore>(store);
        builder.Services.AddScoped<JoinTeamByInvitation>();
        builder.Services.AddScoped<GetTeamInvitation>();
        builder.Services.AddSingleton<IUserContext>(new TestUserContext());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class RecordingMembershipStore(
        TeamMembershipFailure? failure = null,
        string? currentToken = null,
        TeamMembershipFailure? readFailure = null) : ITeamMembershipStore
    {
        public string? InvitationToken { get; private set; }
        public Guid? UserId { get; private set; }

        public Task<TeamMembershipFailure?> JoinByInvitationAsync(
            Guid competitionId,
            string invitationToken,
            Guid userId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            InvitationToken = invitationToken;
            UserId = userId;
            return Task.FromResult(failure);
        }

        public Task<(string? Token, TeamMembershipFailure? Failure)> RotateInvitationAsync(
            Guid competitionId,
            Guid teamId,
            Guid actorId,
            string token,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<(string? Token, TeamMembershipFailure? Failure)> GetInvitationAsync(
            Guid competitionId,
            Guid teamId,
            Guid actorId,
            CancellationToken cancellationToken) => Task.FromResult(
                readFailure is null
                    ? (currentToken, (TeamMembershipFailure?)null)
                    : ((string?)null, readFailure));

        public Task<TeamMembershipFailure?> RemoveMemberAsync(
            Guid competitionId,
            Guid teamId,
            Guid targetUserId,
            Guid actorId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TeamMembershipFailure?> LeaveAsync(
            Guid competitionId,
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TeamMembershipFailure?> TransferCaptainAsync(
            Guid competitionId,
            Guid teamId,
            Guid actorId,
            Guid newCaptainId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestUserContext : IUserContext
    {
        public Guid UserId => JoinTeamByInvitationEndpointTests.UserId;
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
                [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())],
                Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
