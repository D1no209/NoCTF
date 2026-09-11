using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.Platform;
using NoCTF.API.Security;
using NoCTF.Application.Administration;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class PlatformUserTokenHttpTests
{
    private static readonly Guid ActorId = Guid.Parse(
        "e2360300-837e-4c56-a713-2257d00bce79");

    [Test]
    public async Task Administrator_can_issue_without_receiving_a_refresh_cookie()
    {
        var targetId = Guid.NewGuid();
        var store = ActiveTargetStore(targetId);
        var issuer = Substitute.For<IAccessTokenIssuer>();
        issuer.Issue(
                Arg.Any<AuthenticatedUser>(),
                Arg.Any<DateTimeOffset>(),
                TimeSpan.FromHours(1),
                ActorId)
            .Returns(call => new IssuedAccessToken(
                "administrator-issued-token",
                call.ArgAt<DateTimeOffset>(1).AddHours(1),
                Guid.NewGuid()));
        await using var app = await CreateApplicationAsync(store, issuer);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "Administrator");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/platform/users/{targetId}/tokens",
            new { expiresInSeconds = 3600, reason = "support case" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
        var body = await response.Content.ReadFromJsonAsync<IssuePlatformUserTokenResponse>();
        await Assert.That(body!.TargetUserId).IsEqualTo(targetId);
    }

    [Test]
    public async Task Non_administrator_and_impersonated_administrator_cannot_issue()
    {
        var targetId = Guid.NewGuid();
        var store = ActiveTargetStore(targetId);
        var issuer = Substitute.For<IAccessTokenIssuer>();
        await using var app = await CreateApplicationAsync(store, issuer);
        using var client = app.GetTestClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "User");
        using var forbiddenRole = await client.PostAsJsonAsync(
            $"/api/v1/admin/platform/users/{targetId}/tokens",
            new { expiresInSeconds = 3600, reason = "support case" });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "ImpersonatedAdministrator");
        using var forbiddenImpersonation = await client.PostAsJsonAsync(
            $"/api/v1/admin/platform/users/{targetId}/tokens",
            new { expiresInSeconds = 3600, reason = "support case" });

        await Assert.That(forbiddenRole.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(forbiddenImpersonation.StatusCode)
            .IsEqualTo(HttpStatusCode.Forbidden);
        issuer.DidNotReceiveWithAnyArgs().Issue(default!, default, default);
    }

    [Test]
    public async Task Inactive_target_returns_typed_conflict()
    {
        var targetId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var store = Substitute.For<IPlatformAdministrationStore>();
        store.FindUserAsync(targetId, Arg.Any<CancellationToken>()).Returns(new PlatformUserView(
            targetId,
            "disabled",
            "disabled@example.test",
            UserKind.Human,
            UserRole.User,
            UserAccountStatus.Disabled,
            2,
            true,
            now,
            now));
        await using var app = await CreateApplicationAsync(
            store,
            Substitute.For<IAccessTokenIssuer>());
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "Administrator");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/platform/users/{targetId}/tokens",
            new { expiresInSeconds = 3600, reason = "support case" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var body = await response.Content
            .ReadFromJsonAsync<IssuePlatformUserTokenFailureResponse>();
        await Assert.That(body!.Code)
            .IsEqualTo(IssuePlatformUserTokenFailureCode.AccountInactive);
    }

    [Test]
    public async Task Reason_bounds_are_applied_after_trimming_and_missing_users_are_not_found()
    {
        var targetId = Guid.NewGuid();
        var store = ActiveTargetStore(targetId);
        var issuer = Substitute.For<IAccessTokenIssuer>();
        issuer.Issue(
                Arg.Any<AuthenticatedUser>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<TimeSpan>(),
                ActorId)
            .Returns(call => new IssuedAccessToken(
                "administrator-issued-token",
                call.ArgAt<DateTimeOffset>(1).AddHours(1),
                Guid.NewGuid()));
        await using var app = await CreateApplicationAsync(store, issuer);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "Administrator");

        using var accepted = await client.PostAsJsonAsync(
            $"/api/v1/admin/platform/users/{targetId}/tokens",
            new { expiresInSeconds = 3600, reason = $"  {new string('x', 500)}  " });
        using var invalid = await client.PostAsJsonAsync(
            $"/api/v1/admin/platform/users/{targetId}/tokens",
            new { expiresInSeconds = 59, reason = "  no  " });
        using var missing = await client.PostAsJsonAsync(
            $"/api/v1/admin/platform/users/{Guid.NewGuid()}/tokens",
            new { expiresInSeconds = 3600, reason = "support case" });

        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Impersonated_administrator_cannot_list_or_revoke_tokens()
    {
        var targetId = Guid.NewGuid();
        var store = ActiveTargetStore(targetId);
        await using var app = await CreateApplicationAsync(
            store,
            Substitute.For<IAccessTokenIssuer>());
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "ImpersonatedAdministrator");

        using var list = await client.GetAsync(
            $"/api/v1/admin/platform/users/{targetId}/tokens");
        using var revokeOne = await client.DeleteAsync(
            $"/api/v1/admin/platform/users/{targetId}/tokens/{Guid.NewGuid()}");
        using var revokeAll = await client.DeleteAsync(
            $"/api/v1/admin/platform/users/{targetId}/tokens");

        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(revokeOne.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(revokeAll.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    private static IPlatformAdministrationStore ActiveTargetStore(Guid targetId)
    {
        var now = DateTimeOffset.UtcNow;
        var store = Substitute.For<IPlatformAdministrationStore>();
        store.FindUserAsync(targetId, Arg.Any<CancellationToken>()).Returns(new PlatformUserView(
            targetId,
            "target",
            "target@example.test",
            UserKind.Human,
            UserRole.User,
            UserAccountStatus.Active,
            3,
            true,
            now,
            now));
        return store;
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        IPlatformAdministrationStore store,
        IAccessTokenIssuer issuer)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IUserContext, HttpUserContext>();
        builder.Services.AddAuthentication("Bearer")
            .AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(IssuePlatformUserTokenEndpoint).Assembly];
            options.Filter = type => type == typeof(IssuePlatformUserTokenEndpoint)
                || type == typeof(IssuePlatformUserTokenValidator)
                || type == typeof(ListPlatformUserTokensEndpoint)
                || type == typeof(RevokePlatformUserTokenEndpoint)
                || type == typeof(DeletePlatformUserTokensEndpoint);
        });
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(issuer);
        builder.Services.AddScoped<ManagePlatform>();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class TestAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var value = Request.Headers.Authorization.ToString();
            if (!value.StartsWith("Bearer ", StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());
            var token = value[7..];
            var role = token.Contains("Administrator", StringComparison.Ordinal)
                ? "Administrator"
                : "User";
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, ActorId.ToString()),
                new(ClaimTypes.Role, role),
                new("user_kind", "Human")
            };
            if (token.StartsWith("Impersonated", StringComparison.Ordinal))
            {
                claims.Add(new(AccessTokenClaims.Impersonation, "true"));
                claims.Add(new(AccessTokenClaims.ImpersonatorId, Guid.NewGuid().ToString()));
            }
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                Scheme.Name)));
        }
    }
}
