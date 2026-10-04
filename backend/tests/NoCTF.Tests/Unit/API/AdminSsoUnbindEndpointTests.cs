using System.Net;
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
using NSubstitute;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.Tests.Unit.API;

public sealed class AdminSsoUnbindEndpointTests
{
    [Test]
    public async Task EmptyDelete_UnbindsTargetAndRecordsAdministratorActor()
    {
        var targetUserId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var accounts = Substitute.For<ISsoAccountStore>();
        accounts.UnbindAsAdministratorAsync(
                targetUserId,
                actorUserId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(AdminSsoUnbindState.Unbound);

        await using var app = await CreateApp(accounts, actorUserId);
        using var client = app.GetTestClient();
        using var response = await client.DeleteAsync(
            $"/api/v1/admin/platform/users/{targetUserId}/sso-binding");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await accounts.Received(1).UnbindAsAdministratorAsync(
            targetUserId,
            actorUserId,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    private static async Task<WebApplication> CreateApp(
        ISsoAccountStore accounts,
        Guid actorUserId)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies =
            [
                typeof(global::NoCTF.API.Endpoints.Administration.Platform
                    .AdminUnbindSsoIdentityEndpoint).Assembly
            ];
            options.Filter = type => type == typeof(global::NoCTF.API.Endpoints
                .Administration.Platform.AdminUnbindSsoIdentityEndpoint);
        });
        builder.Services.OpenApiDocument();
        builder.Services.AddAuthentication("Bearer")
            .AddScheme<AuthenticationSchemeOptions, TestBearer>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(accounts);
        builder.Services.AddScoped<AdministrativelyUnbindSsoIdentity>();
        builder.Services.AddSingleton<IUserContext>(new TestUserContext(actorUserId));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class TestUserContext(Guid userId) : IUserContext
    {
        public Guid UserId { get; } = userId;
        public bool IsAdministrator => true;
    }

    private sealed class TestBearer(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Role, "Administrator")],
                    Scheme.Name)),
                Scheme.Name)));
    }
}
