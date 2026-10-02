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
using NSubstitute;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.Runtime;
using NoCTF.API.Endpoints.Administration.Teams;
using NoCTF.API.Endpoints.Teams;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Runtime.Flags;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Teams;

namespace NoCTF.Tests.Unit.API;

public sealed class AdminDetailEndpointTests
{
    [Test, Arguments(true), Arguments(false)]
    public async Task Runtime_flag_reads_are_authorized_before_protected_records_are_loaded(bool allowed)
    {
        var reader = Substitute.For<IRuntimeFlagReader>();
        var runtimeId = Guid.NewGuid();
        reader.FindScopeAsync(runtimeId, Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new RuntimeFlagScope(Guid.NewGuid(), null, false));
        reader.ReadAsync(Arg.Any<RuntimeFlagQuery>(), Arg.Any<CancellationToken>()).Returns(new RuntimeFlagPage([], 0));
        await using var app = await CreateApp(reader, allowed);
        using var client = Client(app);
        using var response = await client.GetAsync($"/api/v1/admin/runtimes/{runtimeId}/flags?includeHistory=true&offset=20&limit=5");
        await Assert.That(response.StatusCode).IsEqualTo(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(reader.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IRuntimeFlagReader.ReadAsync)))
            .IsEqualTo(allowed ? 1 : 0);
        if (allowed)
            await reader.Received(1).ReadAsync(Arg.Is<RuntimeFlagQuery>(query => query != null && query.RuntimeInstanceId == runtimeId
                && query.IncludeHistory && query.Offset == 20 && query.Limit == 5), Arg.Any<CancellationToken>());
    }

    [Test, Arguments(true), Arguments(false)]
    public async Task Template_test_flags_require_template_management_even_without_competition_scope(bool allowed)
    {
        var reader = Substitute.For<IRuntimeFlagReader>();
        reader.FindScopeAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new RuntimeFlagScope(null, Guid.NewGuid(), allowed));
        reader.ReadAsync(Arg.Any<RuntimeFlagQuery>(), Arg.Any<CancellationToken>()).Returns(new RuntimeFlagPage([], 0));
        await using var app = await CreateApp(reader, false);
        using var client = Client(app);
        using var response = await client.GetAsync($"/api/v1/admin/runtimes/{Guid.NewGuid()}/flags");
        await Assert.That(response.StatusCode).IsEqualTo(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        await Assert.That(reader.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IRuntimeFlagReader.ReadAsync)))
            .IsEqualTo(allowed ? 1 : 0);
    }

    [Test]
    public async Task Runtime_flag_protocol_preserves_source_state_and_protected_content_without_caching()
    {
        var reader = Substitute.For<IRuntimeFlagReader>();
        reader.FindScopeAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new RuntimeFlagScope(Guid.NewGuid(), null, false));
        var flag = new ChallengeFlagView(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "flag{protected}",
            ChallengeFlagMatchKind.Exact, SpecificationKind.RuntimeInstance, Guid.NewGuid(), null,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, DateTimeOffset.UtcNow.AddHours(-1));
        reader.ReadAsync(Arg.Any<RuntimeFlagQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RuntimeFlagPage([new(flag, RuntimeFlagSource.Instance, RuntimeFlagState.Expired)], 1));
        await using var app = await CreateApp(reader, true);
        using var client = Client(app);
        using var response = await client.GetAsync($"/api/v1/admin/runtimes/{Guid.NewGuid()}/flags?includeHistory=true");
        var body = await response.Content.ReadFromJsonAsync<RuntimeFlagListResponse>();
        await Assert.That(body!.Items.Single().Flag.Flag).IsEqualTo(flag.Flag);
        await Assert.That(body.Items.Single().Source).IsEqualTo(RuntimeFlagSourceProtocol.Instance);
        await Assert.That(body.Items.Single().State).IsEqualTo(RuntimeFlagStateProtocol.Expired);
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(response.Headers.CacheControl.NoCache).IsTrue();
    }

    [Test, Arguments("invalid", HttpStatusCode.BadRequest), Arguments("00000000-0000-0000-0000-000000000000", HttpStatusCode.BadRequest)]
    public async Task Invalid_runtime_ids_are_rejected_before_reading(string id, HttpStatusCode expected)
    {
        var reader = Substitute.For<IRuntimeFlagReader>();
        await using var app = await CreateApp(reader, true);
        using var client = Client(app);
        using var response = await client.GetAsync($"/api/v1/admin/runtimes/{id}/flags");
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(reader.ReceivedCalls().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Missing_runtime_is_not_found_and_unauthenticated_access_does_not_read_scope()
    {
        var reader = Substitute.For<IRuntimeFlagReader>();
        await using var app = await CreateApp(reader, true);
        using var client = app.GetTestClient();
        using var anonymous = await client.GetAsync($"/api/v1/admin/runtimes/{Guid.NewGuid()}/flags");
        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(reader.ReceivedCalls().Count()).IsEqualTo(0);
        client.DefaultRequestHeaders.Add("X-Test-Role", "Organizer");
        using var missing = await client.GetAsync($"/api/v1/admin/runtimes/{Guid.NewGuid()}/flags");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test, Arguments("offset=-1&limit=10"), Arguments("offset=0&limit=201")]
    public async Task Invalid_flag_pagination_is_rejected_before_scope_or_flag_reads(string query)
    {
        var reader = Substitute.For<IRuntimeFlagReader>();
        await using var app = await CreateApp(reader, true);
        using var client = Client(app);
        using var response = await client.GetAsync($"/api/v1/admin/runtimes/{Guid.NewGuid()}/flags?{query}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(reader.ReceivedCalls().Count()).IsEqualTo(0);
    }

    [Test, Arguments(true), Arguments(false)]
    public async Task Team_deep_reads_include_pending_and_internal_teams_under_management_authorization(bool allowed)
    {
        var teams = Substitute.For<ITeamRegistrationStore>();
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var captain = Guid.NewGuid();
        teams.FindAsync(competitionId, teamId, true, Arg.Any<CancellationToken>()).Returns(new TeamView(
            teamId, competitionId, "Pending internal team", null, captain, [captain], TeamRegistrationStatus.Pending,
            false, false, DateTimeOffset.UtcNow));
        await using var app = await CreateApp(Substitute.For<IRuntimeFlagReader>(), allowed, teams);
        using var client = Client(app);
        using var response = await client.GetAsync($"/api/v1/admin/competitions/{competitionId}/teams/{teamId}");
        await Assert.That(response.StatusCode).IsEqualTo(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        if (allowed)
        {
            var body = await response.Content.ReadFromJsonAsync<TeamResponse>();
            await Assert.That(body!.Id).IsEqualTo(teamId);
            await teams.Received(1).FindAsync(competitionId, teamId, true, Arg.Any<CancellationToken>());
        }
        await Assert.That(teams.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ITeamRegistrationStore.FindPublicAsync)))
            .IsEqualTo(0);
    }

    private static HttpClient Client(WebApplication app)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Organizer");
        return client;
    }

    private static async Task<WebApplication> CreateApp(IRuntimeFlagReader reader, bool allowed, ITeamRegistrationStore? teams = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(ListRuntimeFlagsEndpoint).Assembly];
            options.Filter = type => type == typeof(ListRuntimeFlagsEndpoint) || type == typeof(ListRuntimeFlagsValidator)
                || type == typeof(GetAdminTeamEndpoint) || type == typeof(GetAdminTeamValidator);
        });
        builder.Services.SwaggerDocument();
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TestBearer>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(Substitute.For<IUserContext>());
        var authorizer = Substitute.For<ICompetitionModerationAuthorizer>();
        authorizer.CanObserveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(allowed);
        builder.Services.AddSingleton(authorizer);
        builder.Services.AddSingleton(reader);
        builder.Services.AddScoped<QueryRuntimeFlags>();
        builder.Services.AddSingleton(teams ?? Substitute.For<ITeamRegistrationStore>());
        builder.Services.AddScoped<GetTeam>();
        builder.Services.AddSingleton(TimeProvider.System);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class TestBearer(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            return Task.FromResult(role.Length == 0 ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)], Scheme.Name)), Scheme.Name)));
        }
    }
}
