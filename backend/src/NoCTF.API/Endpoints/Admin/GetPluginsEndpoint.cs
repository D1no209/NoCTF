using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class PluginDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

public class GetPluginsEndpoint(IServiceProvider serviceProvider) : Endpoint<EmptyRequest, List<PluginDto>>
{
    public override void Configure()
    {
        Get("/api/admin/plugins");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var plugins = new List<PluginDto>();

        var gameModes = serviceProvider.GetServices<IGameMode>();
        foreach (var gm in gameModes)
        {
            plugins.Add(new PluginDto
            {
                Name = gm.GetType().Name,
                Type = "GameMode",
                Version = gm.GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            });
        }

        var challengeTypes = serviceProvider.GetServices<IChallengeType>();
        foreach (var ct2 in challengeTypes)
        {
            plugins.Add(new PluginDto
            {
                Name = ct2.GetType().Name,
                Type = "ChallengeType",
                Version = ct2.GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            });
        }

        var storageProviders = serviceProvider.GetServices<IStorageProvider>();
        foreach (var sp in storageProviders)
        {
            plugins.Add(new PluginDto
            {
                Name = sp.GetType().Name,
                Type = "StorageProvider",
                Version = sp.GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            });
        }

        var containerManagers = serviceProvider.GetServices<IContainerManager>();
        foreach (var cm in containerManagers)
        {
            plugins.Add(new PluginDto
            {
                Name = cm.GetType().Name,
                Type = "ContainerProvider",
                Version = cm.GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            });
        }

        await SendAsync(plugins, cancellation: ct);
    }
}
