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
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.Tests.Unit.API;

public sealed class SsoBodylessCompletionEndpointTests
{
    [Test]
    public async Task CompleteBinding_EmptyPost_ReachesEndpointInsteadOfReturning415()
    {
        await using var app = await CreateApp();
        using var client = app.GetTestClient();
        using var response = await client.PostAsync(
            $"/api/v1/auth/me/sso-binding/flows/{Guid.NewGuid()}/complete",
            content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.UnsupportedMediaType);
    }

    [Test]
    public async Task CompleteLogin_EmptyPost_ReachesEndpointInsteadOfReturning415()
    {
        await using var app = await CreateApp();
        using var client = app.GetTestClient();
        using var response = await client.PostAsync(
            $"/api/v1/auth/sso/flows/{Guid.NewGuid()}/complete-login",
            content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Gone);
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.UnsupportedMediaType);
    }

    private static async Task<WebApplication> CreateApp()
    {
        var flows = Substitute.For<ISsoFlowStore>();
        flows.ConsumeAuthenticatedAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new SsoFlowReadResult(SsoFlowReadState.NotFound));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(CompleteSsoBindingEndpoint).Assembly];
            options.Filter = type => type == typeof(CompleteSsoBindingEndpoint)
                || type == typeof(CompleteSsoLoginEndpoint);
        });
        builder.Services.OpenApiDocument();
        builder.Services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                "Bearer", _ => { })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                AuthenticationRegistration.SsoFlowScheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddDataProtection();
        builder.Services.AddSingleton<IOptions<RefreshHttpOptions>>(
            Options.Create(new RefreshHttpOptions { RefreshCookieSecure = false }));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(flows);
        builder.Services.AddSingleton(Substitute.For<ISsoAccountStore>());
        builder.Services.AddSingleton(Substitute.For<ISsoProviderRuntimeReader>());
        builder.Services.AddSingleton(Substitute.For<IAccessTokenIssuer>());
        builder.Services.AddSingleton(Substitute.For<IAccountActivityRecorder>());
        builder.Services.AddScoped<CompleteSsoBinding>();
        builder.Services.AddScoped<CompleteSsoLogin>();
        builder.Services.AddScoped<IUserContext, HttpUserContext>();
        builder.Services.AddSingleton<SsoBrowserCorrelation>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            Claim[] claims = Scheme.Name == AuthenticationRegistration.SsoFlowScheme
                ? [new Claim(SsoFlowAuthenticationHandler.BrowserClaim, "test-browser")]
                :
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new Claim("user_kind", "Human")
                ];
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)),
                Scheme.Name)));
        }
    }
}
