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
using NoCTF.Application.Admission;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Storage;
using NoCTF.Application.Common;
using FluentStorage.Storage;
using NoCTF.Infrastructure.Administration;
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
        builder.Services.AddSingleton<IHumanVerificationConfigurationStore>(
            new NoOpHumanVerificationConfigurationStore());
        builder.Services.AddSingleton(new HumanVerificationValidationPolicy(true));
        builder.Services.AddScoped<ManageHumanVerificationConfiguration>();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(GetPlatformConfigurationEndpoint).Assembly];
            options.Filter = type => type == typeof(GetPlatformConfigurationEndpoint)
                || type == typeof(PatchPlatformConfigurationEndpoint)
                || type == typeof(GetPublicGatewayStatusEndpoint);
        });
        var store = Substitute.For<IPublicGatewayPolicyStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(PublicGatewayPolicy.Disabled);
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(Substitute.For<IPublicGatewayStatusStore>());
        builder.Services.AddSingleton(new PublicGatewayCapability("gateway", "runner", ["https://public.example.test"], 32768, 60999, [], 8, true));
        builder.Services.AddScoped<ManagePublicGateway>();
        var platformConfiguration = new NoOpPlatformConfigurationStore();
        builder.Services.AddSingleton<IPlatformConfigurationStore>(platformConfiguration);
        builder.Services.AddSingleton(Substitute.For<IManagedFileUploadRegistry>());
        builder.Services.AddSingleton<IStore>(_ => FluentStorage.StorageFactory.InMemory());
        builder.Services.AddScoped<ManagedFileUploads>();
        builder.Services.AddScoped<ManagePlatformConfiguration>();
        var emailStore = Substitute.For<IEmailVerificationConfigurationStore>();
        emailStore.GetAsync(Arg.Any<CancellationToken>()).Returns(new EmailVerificationConfigurationView(
            false, "https://noctf.test", 60, 60, 30, 60, 3,
            "smtp.noctf.test", 587, NoCTF.Domain.Identity.SmtpSecurityMode.StartTls,
            "", true, "no-reply@noctf.test", "NoCTF", 10, DateTimeOffset.UtcNow));
        builder.Services.AddSingleton(emailStore);
        builder.Services.AddScoped<ManageEmailVerificationConfiguration>();
        builder.Services.AddSingleton<IAtomicAggregatePatch, TestAtomicAggregatePatch>();
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.UseNoCtfEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();
        foreach (var role in new[] { "", "User", "Observer", "Judge", "Manager", "Administrator" })
        {
            client.DefaultRequestHeaders.Authorization = role.Length == 0 ? null : new AuthenticationHeaderValue("Bearer", role);
            var expected = role.Length == 0 ? HttpStatusCode.Unauthorized : role == "Administrator" ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            using var settings = await client.GetAsync("/api/v1/admin/platform/configuration");
            using var status = await client.GetAsync("/api/v1/admin/platform/public-gateway/status");
            await Assert.That(settings.StatusCode).IsEqualTo(expected);
            await Assert.That(status.StatusCode).IsEqualTo(expected);
            using var update = await client.PatchAsJsonAsync("/api/v1/admin/platform/configuration", new
            {
                publicGateway = new PlatformGatewayPatchRequest
                {
                    Enabled = true, ConnectorId = "gateway", PublicOrigin = "https://public.example.test",
                    DirectOrigins = ["https://direct.example.test"], PublicRuntimeHost = "203.0.113.1",
                    DirectRuntimeHostOverride = null, MaxPublishedPorts = 8
                }
            });
            await Assert.That(update.StatusCode).IsEqualTo(role == "Administrator" ? HttpStatusCode.Accepted : expected);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", "Administrator");
        using var unavailableVerification = await client.PatchAsJsonAsync(
            "/api/v1/admin/platform/configuration",
            new
            {
                humanVerification = new
                {
                    enabled = true,
                    runtimeEnabled = true,
                    provider = "Cap",
                    capServerUrl = "https://cap.example.test",
                    capSiteKey = "site-key",
                    turnstileSiteKey = string.Empty,
                    turnstileAllowedHostnames = Array.Empty<string>()
                }
            });
        await Assert.That(unavailableVerification.StatusCode)
            .IsEqualTo(HttpStatusCode.BadRequest);
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

    private sealed class TestAtomicAggregatePatch : IAtomicAggregatePatch
    {
        public async Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<AtomicAggregatePatchDecision<TResult>>> operation,
            CancellationToken cancellationToken = default) =>
            (await operation(cancellationToken)).Result;
    }
}
