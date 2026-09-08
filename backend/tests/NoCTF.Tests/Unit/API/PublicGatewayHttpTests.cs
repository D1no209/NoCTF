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
using NoCTF.Application.Runtime.PublicAccess;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class PublicGatewayHttpTests
{
    [Test]
    public async Task Only_platform_administrators_can_read_or_change_gateway_configuration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(GetPublicGatewayEndpoint).Assembly];
            options.Filter = type => type == typeof(GetPublicGatewayEndpoint) || type == typeof(UpdatePublicGatewayEndpoint)
                || type == typeof(GetPublicGatewayStatusEndpoint);
        });
        var store = Substitute.For<IPublicGatewayPolicyStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(PublicGatewayPolicy.Disabled);
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(Substitute.For<IPublicGatewayStatusStore>());
        builder.Services.AddSingleton(new PublicGatewayCapability("gateway", "runner", ["https://public.example.test"], 32768, 60999, [], 8, true));
        builder.Services.AddScoped<ManagePublicGateway>();
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.UseNoCtfEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();
        foreach (var role in new[] { "", "User", "Observer", "Judge", "Manager", "Administrator" })
        {
            client.DefaultRequestHeaders.Authorization = role.Length == 0 ? null : new AuthenticationHeaderValue("Bearer", role);
            var expected = role.Length == 0 ? HttpStatusCode.Unauthorized : role == "Administrator" ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            using var settings = await client.GetAsync("/api/v1/admin/platform/public-gateway");
            using var status = await client.GetAsync("/api/v1/admin/platform/public-gateway/status");
            await Assert.That(settings.StatusCode).IsEqualTo(expected);
            await Assert.That(status.StatusCode).IsEqualTo(expected);
            using var update = await client.PutAsJsonAsync("/api/v1/admin/platform/public-gateway", new UpdatePublicGatewayRequest
            {
                Enabled = true, ConnectorId = "gateway", PublicOrigin = "https://public.example.test",
                DirectOrigins = ["https://direct.example.test"], PublicRuntimeHost = "203.0.113.1", MaxPublishedPorts = 8
            });
            await Assert.That(update.StatusCode).IsEqualTo(role == "Administrator" ? HttpStatusCode.Accepted : expected);
        }
        await store.Received(1).SaveAsync(Arg.Any<PublicGatewayPolicy>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, header[7..]), new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString())], "Bearer");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Bearer")));
        }
    }
}
