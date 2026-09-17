using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Composition;

namespace NoCTF.Tests.Unit.API;

public sealed class StaticAssetDeliveryHttpTests
{
    [Test]
    public async Task Fingerprinted_assets_are_compressed_and_immutable_while_the_spa_document_is_not_cached()
    {
        var webRoot = Directory.CreateTempSubdirectory("noctf-static-assets-");
        try
        {
            var nuxtDirectory = Directory.CreateDirectory(
                Path.Combine(webRoot.FullName, "_nuxt"));
            await File.WriteAllTextAsync(
                Path.Combine(nuxtDirectory.FullName, "app.12345678.js"),
                string.Concat(Enumerable.Repeat("export const value = 'compressible';\n", 256)));
            await File.WriteAllTextAsync(
                Path.Combine(webRoot.FullName, "index.html"),
                "<!doctype html><html><body>NoCTF</body></html>");

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                WebRootPath = webRoot.FullName
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddNoCtfStaticAssetDelivery();
            await using var app = builder.Build();
            app.UseNoCtfStaticAssetDelivery();
            await app.StartAsync();

            using var client = app.GetTestClient();
            client.BaseAddress = new Uri("https://noctf.example/");
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

            using var asset = await client.GetAsync("/_nuxt/app.12345678.js");
            await Assert.That(asset.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(asset.Content.Headers.ContentEncoding).Contains("br");
            await Assert.That(asset.Headers.Vary).Contains("Accept-Encoding");
            await Assert.That(asset.Headers.CacheControl!.Public).IsTrue();
            await Assert.That(asset.Headers.CacheControl.MaxAge).IsEqualTo(TimeSpan.FromDays(365));
            await Assert.That(asset.Headers.CacheControl.Extensions.Any(item =>
                string.Equals(item.Name, "immutable", StringComparison.OrdinalIgnoreCase))).IsTrue();

            using var document = await client.GetAsync("/index.html");
            await Assert.That(document.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(document.Headers.CacheControl!.NoCache).IsTrue();
        }
        finally
        {
            webRoot.Delete(recursive: true);
        }
    }
}
