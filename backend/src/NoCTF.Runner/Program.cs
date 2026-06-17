using NoCTF.PluginBase;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(
    _ => new NoCTF.Container.Docker.DockerProvider(builder.Configuration["Docker:Host"]));
builder.Services.AddScoped<IContainerManager, NoCTF.Container.Docker.DockerManager>();

var allowedRegistries = builder.Configuration
    .GetSection("Runner:AllowedRegistries")
    .Get<string[]>() ?? [];

var app = builder.Build();

app.MapGet("/runner/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/runner/containers", async (
    ContainerConfig config,
    IContainerManager manager,
    CancellationToken ct) =>
{
    if (!ImageAllowed(config.Image, allowedRegistries))
        return Results.BadRequest(new { code = "image_registry_not_allowed" });

    return Results.Ok(await manager.CreateContainerAsync(config, ct));
});

app.MapPost("/runner/containers/destroy", async (
    ContainerInstance container,
    IContainerManager manager,
    CancellationToken ct) =>
{
    await manager.DestroyContainerAsync(container, ct);
    return Results.NoContent();
});

app.MapPost("/runner/jobs/one-shot", async (
    ContainerConfig config,
    IContainerManager manager,
    CancellationToken ct) =>
{
    if (!ImageAllowed(config.Image, allowedRegistries))
        return Results.BadRequest(new { code = "image_registry_not_allowed" });

    return Results.Ok(await manager.RunContainerAsync(config, ct));
});

app.MapPost("/runner/compose/up", async (
    ComposeConfig config,
    IContainerManager manager,
    CancellationToken ct) =>
{
    return Results.Ok(await manager.ComposeUpAsync(config, ct));
});

app.MapPost("/runner/compose/down", async (
    ComposeDeployment deployment,
    IContainerManager manager,
    CancellationToken ct) =>
{
    await manager.ComposeDownAsync(deployment, ct);
    return Results.NoContent();
});

await app.RunAsync();

static bool ImageAllowed(string image, IReadOnlyCollection<string> allowedRegistries)
{
    if (allowedRegistries.Count == 0)
        return true;

    return allowedRegistries.Any(prefix =>
        image.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
