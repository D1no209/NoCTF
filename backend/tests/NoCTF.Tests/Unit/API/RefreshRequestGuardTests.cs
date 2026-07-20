using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NoCTF.API.Endpoints.Authentication;

namespace NoCTF.Tests.Unit.Api;

public sealed class RefreshRequestGuardTests
{
    [Test]
    public async Task IsAllowed_UsesConfiguredFrontendOrigin()
    {
        var request = Request("https://api.noctf.example", "https://app.noctf.example");
        var configuration = Configuration(["https://app.noctf.example"]);

        await Assert.That(RefreshRequestGuard.IsAllowed(request, configuration)).IsTrue();
    }

    [Test]
    public async Task IsAllowed_RejectsAnOriginOutsideTheConfiguredDeploymentDomains()
    {
        var request = Request("https://api.noctf.example", "https://untrusted.example");
        var configuration = Configuration(["https://app.noctf.example"]);

        await Assert.That(RefreshRequestGuard.IsAllowed(request, configuration)).IsFalse();
    }

    [Test]
    public async Task IsAllowed_AcceptsTheApiOriginWithoutAnExplicitConfigurationEntry()
    {
        var request = Request("https://api.noctf.example", "https://api.noctf.example");

        await Assert.That(RefreshRequestGuard.IsAllowed(request, Configuration([]))).IsTrue();
    }

    [Test]
    public async Task IsAllowed_UsesRefererWhenOriginIsNotPresent()
    {
        var request = Request("https://api.noctf.example", null);
        request.Headers.Referer = "https://app.noctf.example/auth/refresh";

        await Assert.That(RefreshRequestGuard.IsAllowed(
            request,
            Configuration(["https://app.noctf.example"]))).IsTrue();
    }

    private static HttpRequest Request(string apiOrigin, string? origin)
    {
        var context = new DefaultHttpContext();
        var uri = new Uri(apiOrigin);
        context.Request.Scheme = uri.Scheme;
        context.Request.Host = new HostString(uri.Host, uri.Port);
        if (origin is not null) context.Request.Headers.Origin = origin;
        return context.Request;
    }

    private static IConfiguration Configuration(string[] allowedOrigins) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(allowedOrigins.Select((origin, index) =>
                new KeyValuePair<string, string?>($"Authentication:RefreshAllowedOrigins:{index}", origin)))
            .Build();
}
