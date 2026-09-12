using System.Net;
using FluentStorage.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Composition;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Storage;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class SpaDocumentMetadataHttpTests
{
    [Test]
    public async Task Spa_document_contains_current_safe_share_metadata_before_javascript_runs()
    {
        var webRoot = Directory.CreateTempSubdirectory("noctf-spa-metadata-");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(webRoot.FullName, "index.html"),
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head>"
                + "<body><div id=\"__nuxt\"></div></body></html>");
            var logoFileId = Guid.Parse("12345678-1234-1234-1234-1234567890ab");
            var current = new PlatformConfigurationView(
                "No<CTF & Arena",
                "Solve \"fast\" <script>alert('x')</script>",
                logoFileId,
                DateTimeOffset.UtcNow);
            var settings = Substitute.For<IPlatformConfigurationStore>();
            settings.GetAsync(Arg.Any<CancellationToken>()).Returns(_ => current);
            var objects = Substitute.For<IStore>();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                WebRootPath = webRoot.FullName
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddRouting();
            builder.Services.AddSingleton(settings);
            builder.Services.AddSingleton(objects);
            builder.Services.AddSingleton(new ManagedFileUploads(
                Substitute.For<IManagedFileUploadRegistry>(),
                objects));
            builder.Services.AddScoped<ManagePlatformConfiguration>();
            await using var app = builder.Build();
            app.UseStaticFiles();
            app.UseMiddleware<SpaDocumentMetadataMiddleware>();
            app.MapGet(
                    "/api/v1/platform/logo",
                    () => TypedResults.NotFound())
                .WithName("PlatformLogo_Get");
            await app.StartAsync();
            using var client = app.GetTestClient();
            client.BaseAddress = new Uri("https://noctf.example/");

            using var response = await client.GetAsync(
                "/competitions?competition=one&view=full");
            var document = await response.Content.ReadAsStringAsync();

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Content.Headers.ContentType?.MediaType)
                .IsEqualTo("text/html");
            await Assert.That(response.Headers.CacheControl!.NoCache).IsTrue();
            await Assert.That(response.Headers.Vary).Contains("Accept");
            await Assert.That(document).Contains("<div id=\"__nuxt\"></div>");
            await Assert.That(document).Contains("<title>No&lt;CTF &amp; Arena</title>");
            await Assert.That(document).Contains(
                "<meta name=\"description\" content=\"Solve &quot;fast&quot; &lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;\">");
            await Assert.That(document).Contains("property=\"og:description\"");
            await Assert.That(document).Contains(
                "property=\"og:url\" content=\"https://noctf.example/competitions?competition=one&amp;view=full\"");
            await Assert.That(document).Contains(
                "property=\"og:image\" content=\"https://noctf.example/api/v1/platform/logo?v=123456781234123412341234567890ab\"");
            await Assert.That(document).Contains("type=\"application/ld+json\"");
            await Assert.That(document).Contains("\"@type\":\"WebSite\"");
            await Assert.That(document).Contains("\"url\":\"https://noctf.example/\"");
            await Assert.That(document).DoesNotContain("<script>alert('x')</script>");

            current = current with { Description = "Updated platform description" };
            using var crawlerRequest = new HttpRequestMessage(HttpMethod.Get, "/");
            crawlerRequest.Headers.Accept.ParseAdd("*/*");
            using var updatedResponse = await client.SendAsync(crawlerRequest);
            var updatedDocument = await updatedResponse.Content.ReadAsStringAsync();
            await Assert.That(updatedDocument).Contains("Updated platform description");
            await Assert.That(updatedDocument).DoesNotContain("Solve &quot;fast&quot;");

            using var apiResponse = await client.GetAsync("/api/v1/missing");
            await Assert.That(apiResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(await apiResponse.Content.ReadAsStringAsync())
                .DoesNotContain("noctf-platform-metadata");
        }
        finally
        {
            webRoot.Delete(recursive: true);
        }
    }
}
