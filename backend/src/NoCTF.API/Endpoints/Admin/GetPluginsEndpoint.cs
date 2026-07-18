using FastEndpoints;
using NoCTF.Application.Plugins;
using NoCTF.Core;

namespace NoCTF.API.Endpoints.Admin;

public class PluginDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

public class GetPluginsEndpoint(PluginCatalog pluginCatalog) : Endpoint<EmptyRequest, List<PluginDto>>
{
    public override void Configure()
    {
        Get("/api/admin/plugins");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var plugins = pluginCatalog.Plugins
            .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .Select(plugin => new PluginDto
            {
                Name = plugin.Name,
                Type = "PluginModule",
                Version = plugin.Version,
            })
            .ToList();

        plugins.Insert(0, new PluginDto
        {
            Name = "NoCTF.Core",
            Type = "CoreEngine",
            Version = typeof(Competition).Assembly.GetName().Version?.ToString() ?? "unknown",
        });

        await SendAsync(plugins, cancellation: ct);
    }
}
