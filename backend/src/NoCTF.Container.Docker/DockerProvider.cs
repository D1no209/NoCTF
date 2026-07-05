using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.PluginBase;

namespace NoCTF.Container.Docker;

public class DockerProvider : IContainerProvider<DockerClient, DockerContainerMetadata>
{
    private readonly DockerClient _client;

    public DockerProvider(string? dockerHost = null)
    {
        var configuration = dockerHost is not null
            ? new DockerClientConfiguration(new Uri(dockerHost))
            : new DockerClientConfiguration();
        _client = configuration.CreateClient();
    }

    public DockerClient CreateClient() => _client;

    public async Task<DockerContainerMetadata> CreateContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
    {
        var ownsNetwork = string.IsNullOrWhiteSpace(config.NetworkName);
        var networkName = ownsNetwork
            ? $"noctf-{Guid.NewGuid():N}"
            : config.NetworkName!;
        if (ownsNetwork)
        {
            await _client.Networks.CreateNetworkAsync(
                new NetworksCreateParameters
                {
                    Name = networkName,
                    Driver = "bridge",
                    Labels = config.Labels ?? new Dictionary<string, string>()
                },
                cancellationToken);
        }

        var networkedConfig = config with { NetworkName = networkName };
        var createParams = new CreateContainerParameters
        {
            Image = networkedConfig.Image,
            Cmd = string.IsNullOrWhiteSpace(networkedConfig.Command)
                ? null
                : networkedConfig.Entrypoint is { Count: > 0 }
                    ? [networkedConfig.Command]
                    : networkedConfig.Command.Split(' '),
            Entrypoint = networkedConfig.Entrypoint?.ToList(),
            Env = networkedConfig.EnvironmentVariables?.Select(kvp => $"{kvp.Key}={kvp.Value}").ToList() ?? [],
            Labels = networkedConfig.Labels ?? new Dictionary<string, string>(),
            User = DockerHostConfigFactory.ResolveUser(networkedConfig),
            ExposedPorts = BuildExposedPorts(networkedConfig),
            HostConfig = DockerHostConfigFactory.Create(
                networkedConfig,
                networkedConfig.PortMappings is null || networkedConfig.PortMappings.Count == 0,
                BuildPortBindings(networkedConfig)),
            NetworkingConfig = BuildNetworkingConfig(networkName, networkedConfig.NetworkAliases)
        };

        string? containerId = null;
        try
        {
            await EnsureImageAsync(networkedConfig.Image, cancellationToken);

            var createResponse = await _client.Containers.CreateContainerAsync(createParams, cancellationToken);
            containerId = createResponse.ID;
            await _client.Containers.StartContainerAsync(containerId, null, cancellationToken);

            var inspect = await _client.Containers.InspectContainerAsync(containerId, cancellationToken);

            return new DockerContainerMetadata(
                ContainerId: containerId,
                Image: networkedConfig.Image,
                Status: inspect.State.Status,
                Ports: ReadPublishedPorts(inspect),
                NetworkName: ownsNetwork ? networkName : null
            );
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(containerId))
                await RemoveContainerBestEffortAsync(containerId, cancellationToken);
            if (ownsNetwork)
                await RemoveNetworkBestEffortAsync(networkName, cancellationToken);
            throw;
        }
    }

    private static IDictionary<string, EmptyStruct>? BuildExposedPorts(ContainerConfig config)
    {
        if (config.PortMappings is null || config.PortMappings.Count == 0) return null;
        return config.PortMappings.Keys.ToDictionary(port => $"{port}/tcp", _ => new EmptyStruct());
    }

    private static NetworkingConfig BuildNetworkingConfig(string networkName, IReadOnlyList<string>? aliases)
        => new()
        {
            EndpointsConfig = new Dictionary<string, EndpointSettings>
            {
                [networkName] = new()
                {
                    Aliases = aliases?
                        .Where(alias => !string.IsNullOrWhiteSpace(alias))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                }
            }
        };

    private static IDictionary<string, IList<PortBinding>> BuildPortBindings(ContainerConfig config)
    {
        if (config.PortMappings is null || config.PortMappings.Count == 0)
            return new Dictionary<string, IList<PortBinding>>();

        return config.PortMappings.ToDictionary(
            kvp => $"{kvp.Key}/tcp",
            kvp => (IList<PortBinding>)new List<PortBinding>
            {
                new()
                {
                    HostPort = kvp.Value > 0 ? kvp.Value.ToString() : string.Empty
                }
            });
    }

    private static Dictionary<int, int> ReadPublishedPorts(ContainerInspectResponse inspect)
    {
        var result = new Dictionary<int, int>();
        if (inspect.NetworkSettings.Ports is null) return result;

        foreach (var (portKey, bindings) in inspect.NetworkSettings.Ports)
        {
            if (!int.TryParse(portKey.Split('/')[0], out var containerPort)) continue;

            var hostPortText = bindings?.FirstOrDefault()?.HostPort;
            if (!int.TryParse(hostPortText, out var hostPort) || hostPort <= 0) continue;

            result[containerPort] = hostPort;
        }

        return result;
    }

    private async Task EnsureImageAsync(string image, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Images.InspectImageAsync(image, cancellationToken);
        }
        catch (DockerImageNotFoundException)
        {
            await _client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = image },
                null,
                new Progress<JSONMessage>(),
                cancellationToken);
        }
    }

    public async Task DestroyContainerAsync(DockerContainerMetadata metadata, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(
                metadata.ContainerId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // Destroy is idempotent from the platform's point of view; stale DB rows should not block recreation.
        }

        if (!string.IsNullOrWhiteSpace(metadata.NetworkName))
            await RemoveNetworkBestEffortAsync(metadata.NetworkName, cancellationToken);
    }

    private async Task RemoveNetworkBestEffortAsync(string networkName, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Networks.DeleteNetworkAsync(networkName, cancellationToken);
        }
        catch
        {
            // Network cleanup should not make container destroy non-idempotent.
        }
    }

    private async Task RemoveContainerBestEffortAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
        catch
        {
            // The create path is already failing; best-effort cleanup must not hide the original error.
        }
    }
}
