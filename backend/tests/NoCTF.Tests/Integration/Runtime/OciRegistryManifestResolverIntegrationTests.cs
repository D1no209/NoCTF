using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Infrastructure.Challenges.Images;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class OciRegistryManifestResolverIntegrationTests
{
    [Test]
    public async Task Resolver_pins_a_tag_from_a_live_controlled_http_registry(
        CancellationToken cancellationToken)
    {
        const string manifest =
            """
            {
              "schemaVersion": 2,
              "mediaType": "application/vnd.oci.image.manifest.v1+json",
              "config": {
                "mediaType": "application/vnd.oci.image.config.v1+json",
                "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "size": 2
              },
              "layers": []
            }
            """;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0));
        await using var registry = builder.Build();
        registry.MapGet(
            "/v2/acme/app/manifests/v1",
            () => Results.Text(
                manifest,
                "application/vnd.oci.image.manifest.v1+json",
                Encoding.UTF8));
        await registry.StartAsync(cancellationToken);

        var address = registry.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        var registryUri = new Uri(address);
        using var client = new HttpClient();
        var resolver = new OciRegistryManifestResolver(new HttpClientFactory(client));

        var result = await resolver.ResolveAsync(
            $"127.0.0.1:{registryUri.Port}/acme/app:v1",
            cancellationToken);

        var expectedDigest =
            $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant()}";
        await Assert.That(result.PinnedImage)
            .IsEqualTo($"127.0.0.1:{registryUri.Port}/acme/app@{expectedDigest}");
        await registry.StopAsync(cancellationToken);
    }

    private sealed class HttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
