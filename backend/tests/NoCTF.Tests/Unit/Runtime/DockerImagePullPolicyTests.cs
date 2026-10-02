using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class DockerImagePullPolicyTests
{
    [Test, Arguments("webcry"), Arguments("webcry:latest"), Timeout(30_000)]
    public async Task Installed_unqualified_image_is_created_without_a_registry_pull(
        string image, CancellationToken ct)
    {
        await using var api = await RecordingDockerApi.StartAsync(true, ct);
        using var lifecycle = new DockerContainerLifecycle(new(Endpoint: api.Endpoint, NetworkName: "none"));
        await lifecycle.CreateAsync(Request(image), ct);
        await Assert.That(api.Calls).Contains("create");
        await Assert.That(api.Calls).DoesNotContain("pull");
        await Assert.That(api.CreatedWithCachedImage).IsTrue();
    }

    [Test, Timeout(30_000)]
    public async Task Missing_unqualified_image_is_pulled_before_creation(CancellationToken ct)
    {
        await using var api = await RecordingDockerApi.StartAsync(false, ct, cachedImageExists: false);
        using var lifecycle = new DockerContainerLifecycle(new(Endpoint: api.Endpoint, NetworkName: "none",
            RegistryConfigDirectory: Path.Combine(Path.GetTempPath(), $"noctf-pull-auth-{Guid.NewGuid():N}")));
        await lifecycle.CreateAsync(Request("webcry"), ct);
        await Assert.That(string.Join(',', api.Calls.Where(call => call is "pull" or "create")))
            .IsEqualTo("pull,create");
        await Assert.That(api.PulledImages.Single()).IsEqualTo("webcry");
        await Assert.That(api.CreatedWithCachedImage).IsFalse();
    }

    [Test, Timeout(30_000)]
    public async Task Missing_unqualified_image_pull_failure_does_not_create_a_container(CancellationToken ct)
    {
        await using var api = await RecordingDockerApi.StartAsync(true, ct, cachedImageExists: false);
        using var lifecycle = new DockerContainerLifecycle(new(Endpoint: api.Endpoint, NetworkName: "none",
            RegistryConfigDirectory: Path.Combine(Path.GetTempPath(), $"noctf-pull-auth-{Guid.NewGuid():N}")));
        await Assert.That(async () => await lifecycle.CreateAsync(Request("webcry"), ct)).Throws<InvalidOperationException>();
        await Assert.That(api.Calls).Contains("pull");
        await Assert.That(api.Calls).DoesNotContain("create");
    }

    [Test, Timeout(30_000)]
    public async Task Cached_tag_is_pulled_again_before_each_new_container(CancellationToken ct)
    {
        await using var api = await RecordingDockerApi.StartAsync(false, ct);
        using var lifecycle = new DockerContainerLifecycle(new(Endpoint: api.Endpoint, NetworkName: "none",
            RegistryConfigDirectory: Path.Combine(Path.GetTempPath(), $"noctf-pull-auth-{Guid.NewGuid():N}")));
        await lifecycle.CreateAsync(Request(), ct);
        await lifecycle.CreateAsync(Request(), ct);
        await Assert.That(string.Join(',', api.Calls.Where(call => call is "pull" or "create")))
            .IsEqualTo("pull,create,pull,create");
        await Assert.That(api.PulledImages).Count().IsEqualTo(2);
        await Assert.That(api.PulledImages.All(image => image == "registry.example/challenge:latest")).IsTrue();
        await Assert.That(api.CreatedWithCachedImage).IsFalse();
    }

    [Test, Timeout(30_000)]
    public async Task Pull_failure_does_not_create_from_an_existing_cached_tag(CancellationToken ct)
    {
        await using var api = await RecordingDockerApi.StartAsync(true, ct);
        using var lifecycle = new DockerContainerLifecycle(new(Endpoint: api.Endpoint, NetworkName: "none",
            RegistryConfigDirectory: Path.Combine(Path.GetTempPath(), $"noctf-pull-auth-{Guid.NewGuid():N}")));
        await Assert.That(async () => await lifecycle.CreateAsync(Request(), ct)).Throws<InvalidOperationException>();
        await Assert.That(api.Calls).Contains("pull");
        await Assert.That(api.Calls).DoesNotContain("create");
    }

    private static ContainerRequest Request(string image = "registry.example/challenge:latest") => new(Guid.NewGuid(), RuntimeProvider.Docker,
        image, ["sleep", "60"], new Dictionary<string, string>(),
        new Dictionary<string, string>(), new Dictionary<int, int>(), new(64 * 1024 * 1024, 200, 64), null);

    private sealed class RecordingDockerApi(WebApplication app, bool failPull, bool cachedImageExists) : IAsyncDisposable
    {
        public string Endpoint { get; private set; } = string.Empty;
        public List<string> Calls { get; } = [];
        public List<string> PulledImages { get; } = [];
        public bool CreatedWithCachedImage { get; private set; }
        private bool refreshed;

        public static async Task<RecordingDockerApi> StartAsync(bool failPull, CancellationToken ct, bool cachedImageExists = true)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            var api = new RecordingDockerApi(app, failPull, cachedImageExists);
            app.Run(api.HandleAsync);
            await app.StartAsync(ct);
            api.Endpoint = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            return api;
        }

        private async Task HandleAsync(HttpContext context)
        {
            var path = context.Request.Path.Value!;
            if (path.EndsWith("/version", StringComparison.Ordinal))
                await JsonAsync(context, new { ApiVersion = "1.47", MinAPIVersion = "1.24", Version = "27.0.0" });
            else if (path.EndsWith("/_ping", StringComparison.Ordinal))
                await context.Response.WriteAsync("OK");
            else if (path.EndsWith("/images/create", StringComparison.Ordinal))
            {
                Calls.Add("pull");
                PulledImages.Add(context.Request.Query["fromImage"].ToString());
                refreshed = !failPull;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(failPull
                    ? "{\"errorDetail\":{\"message\":\"registry unavailable\"},\"error\":\"registry unavailable\"}\n"
                    : "{\"status\":\"Downloaded newer image\"}\n");
            }
            else if (path.Contains("/images/", StringComparison.Ordinal) && path.EndsWith("/json", StringComparison.Ordinal))
            {
                if (!cachedImageExists && !refreshed)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await JsonAsync(context, new { message = "No such image" });
                }
                else
                    await JsonAsync(context, new { Id = refreshed ? "fresh-image" : "cached-image", Os = "linux", Config = new { User = "" } });
            }
            else if (path.EndsWith("/containers/create", StringComparison.Ordinal))
            {
                Calls.Add("create");
                CreatedWithCachedImage |= !refreshed;
                using var body = await JsonDocument.ParseAsync(context.Request.Body);
                await JsonAsync(context, new { Id = "container-" + Calls.Count, Warnings = Array.Empty<string>() });
                refreshed = false;
            }
            else if (path.Contains("/containers/", StringComparison.Ordinal) && path.EndsWith("/json", StringComparison.Ordinal))
                await JsonAsync(context, new { Id = "container", State = new { Status = "running" }, NetworkSettings = new { Ports = new { } } });
            else
                context.Response.StatusCode = StatusCodes.Status204NoContent;
        }

        private static Task JsonAsync<T>(HttpContext context, T body)
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(JsonSerializer.Serialize(body));
        }

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }
}
