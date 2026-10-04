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
using NSubstitute;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.Platform;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.API;

public sealed class SsoAdministrationTestEndpointTests
{
    [Test]
    public async Task ConnectionTest_EmptyPost_ReachesProviderTester()
    {
        var providerId = Guid.NewGuid();
        var connectionTester = Substitute.For<ISsoProviderConnectionTester>();
        connectionTester.TestAsync(providerId, Arg.Any<CancellationToken>()).Returns(
            new SsoProviderConnectionTestResult(
                true,
                SsoProtocol.Oidc,
                "https://id.example.test",
                null,
                null));

        await using var app = await CreateApp(connectionTester: connectionTester);
        using var client = AdministratorClient(app);
        using var response = await client.PostAsync(
            $"/api/v1/admin/platform/sso/providers/{providerId}/connection-tests",
            content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SsoProviderConnectionTestResponse>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Succeeded).IsTrue();
        await connectionTester.Received(1).TestAsync(providerId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AuthenticationTest_EmptyPost_CreatesBrowserFlow()
    {
        var providerId = Guid.NewGuid();
        var providers = Substitute.For<ISsoProviderRuntimeReader>();
        providers.FindAsync(providerId, Arg.Any<CancellationToken>()).Returns(
            new SsoProviderRuntimeConfiguration(
                false,
                "https://ctf.example.test",
                providerId,
                "Example OIDC",
                SsoProtocol.Oidc,
                false,
                true,
                true,
                10,
                ["id.example.test"],
                "fingerprint",
                new(
                    "https://id.example.test",
                    "https://id.example.test/.well-known/openid-configuration",
                    "noctf",
                    "secret",
                    ["openid"],
                    false,
                    "name"),
                null));
        var adapter = Substitute.For<ISsoProtocolAdapter>();
        adapter.Protocol.Returns(SsoProtocol.Oidc);
        adapter.CreateAuthorizationAsync(
                Arg.Any<SsoProviderRuntimeConfiguration>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new SsoAuthorizationRequest(
                new Uri("https://id.example.test/authorize?client_id=noctf"),
                "nonce",
                "verifier"));
        var flows = Substitute.For<ISsoFlowStore>();
        flows.CreateAsync(Arg.Any<SsoFlowRecord>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await using var app = await CreateApp(providers: providers, adapter: adapter, flows: flows);
        using var client = AdministratorClient(app);
        using var response = await client.PostAsync(
            $"/api/v1/admin/platform/sso/providers/{providerId}/authentication-tests",
            content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BeginSsoLoginResponse>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.AuthorizationUrl)
            .IsEqualTo("https://id.example.test/authorize?client_id=noctf");
        await flows.Received(1).CreateAsync(
            Arg.Is<SsoFlowRecord>(flow =>
                flow != null
                && flow.ProviderId == providerId
                && flow.Intent == SsoFlowIntent.AdministratorTest),
            Arg.Any<CancellationToken>());
    }

    private static HttpClient AdministratorClient(WebApplication app)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        return client;
    }

    private static async Task<WebApplication> CreateApp(
        ISsoProviderConnectionTester? connectionTester = null,
        ISsoProviderRuntimeReader? providers = null,
        ISsoProtocolAdapter? adapter = null,
        ISsoFlowStore? flows = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(TestSsoProviderConnectionEndpoint).Assembly];
            options.Filter = type => type == typeof(TestSsoProviderConnectionEndpoint)
                || type == typeof(BeginSsoAuthenticationTestEndpoint);
        });
        builder.Services.OpenApiDocument();
        builder.Services.AddAuthentication("Bearer")
            .AddScheme<AuthenticationSchemeOptions, TestBearer>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddDataProtection();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(Substitute.For<ISsoConfigurationStore>());
        builder.Services.AddSingleton(connectionTester ?? Substitute.For<ISsoProviderConnectionTester>());
        builder.Services.AddSingleton(providers ?? Substitute.For<ISsoProviderRuntimeReader>());
        builder.Services.AddSingleton(adapter ?? Substitute.For<ISsoProtocolAdapter>());
        builder.Services.AddSingleton(flows ?? Substitute.For<ISsoFlowStore>());
        builder.Services.AddScoped<ManageSsoProviders>();
        builder.Services.AddScoped<BeginSsoFlow>();
        builder.Services.AddSingleton<SsoBrowserCorrelation>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class TestBearer(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            return Task.FromResult(role.Length == 0
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                            new Claim(ClaimTypes.Role, role)
                        ],
                        Scheme.Name)),
                    Scheme.Name)));
        }
    }
}
