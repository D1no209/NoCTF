using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.Application.Storage;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.Tests.Unit.API;

public sealed class ChangePasswordEndpointTests
{
    private static readonly Guid UserId =
        Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7286");

    [Test]
    public async Task Current_password_failure_is_a_stable_typed_conflict()
    {
        await using var app = await CreateApplicationAsync(
            ChangePasswordState.CurrentPasswordInvalid);
        using var client = app.GetTestClient();

        using var response = await client.PutAsJsonAsync(
            "/api/v1/auth/password",
            new { currentPassword = "wrong-pass", newPassword = "new-pass" });
        var failure = await response.Content.ReadFromJsonAsync<ChangePasswordFailureResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code)
            .IsEqualTo(ChangePasswordFailureCode.CurrentPasswordInvalid);
    }

    [Test]
    public async Task Successful_change_clears_the_current_refresh_cookie()
    {
        await using var app = await CreateApplicationAsync(ChangePasswordState.Changed);
        using var client = app.GetTestClient();

        using var response = await client.PutAsJsonAsync(
            "/api/v1/auth/password",
            new { currentPassword = "old-pass", newPassword = "new-pass" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var cookies)).IsTrue();
        var cookie = cookies?.Single() ?? string.Empty;
        await Assert.That(cookie).Contains("__Secure-noctf_refresh=");
        await Assert.That(cookie).Contains("path=/api/v1/auth");
    }

    [Test]
    public async Task Http_test_deployment_clears_the_unprefixed_refresh_cookie()
    {
        await using var app = await CreateApplicationAsync(
            ChangePasswordState.Changed,
            refreshCookieSecure: false);
        using var client = app.GetTestClient();

        using var response = await client.PutAsJsonAsync(
            "/api/v1/auth/password",
            new { currentPassword = "old-pass", newPassword = "new-pass" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        await Assert.That(cookie).Contains("noctf_refresh=");
        await Assert.That(cookie).DoesNotContain("__Secure-noctf_refresh=");
        await Assert.That(cookie).DoesNotContain("; secure");
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        ChangePasswordState state,
        bool refreshCookieSecure = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:RefreshCookieSecure"] = refreshCookieSecure.ToString()
        });
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(ChangePasswordEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(ChangePasswordEndpoint)
                || type == typeof(ChangePasswordValidator);
        });
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
        builder.Services.AddSingleton<IUserAuthenticationStore>(new PasswordStore(state));
        builder.Services.AddScoped<ChangePassword>();
        builder.Services.AddSingleton<IUserContext>(new TestUserContext());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(Options.Create(new RefreshHttpOptions
        {
            RefreshCookieSecure = refreshCookieSecure
        }));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class PasswordStore(ChangePasswordState state) : IUserAuthenticationStore
    {
        public Task<ChangePasswordState> ChangePasswordAsync(
            Guid userId,
            string currentPassword,
            string newPassword,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(state);

        public Task<AuthenticatedUser?> FindByLoginAsync(string login, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<UserProfile?> UpdateProfileAsync(Guid userId, string? description,
            DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<UserAvatarReplacement?> ReplaceAvatarAsync(Guid userId, Guid fileId,
            DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<string?> GetAvatarObjectKeyAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<CreateUserState> CreateAsync(Guid userId, string userName, string email,
            string password, bool emailVerified, DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IncrementTokenVersionAsync(Guid userId, DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestUserContext : IUserContext
    {
        public Guid UserId => ChangePasswordEndpointTests.UserId;
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
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
