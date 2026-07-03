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
        var createParams = new CreateContainerParameters
        {
            Image = config.Image,
            Cmd = string.IsNullOrWhiteSpace(config.Command)
                ? null
                : config.Entrypoint is { Count: > 0 }
                    ? [config.Command]
                    : config.Command.Split(' '),
            Entrypoint = config.Entrypoint?.ToList(),
            Env = config.EnvironmentVariables?.Select(kvp => $"{kvp.Key}={kvp.Value}").ToList() ?? [],
            Labels = config.Labels ?? new Dictionary<string, string>(),
            ExposedPorts = BuildExposedPorts(config),
            HostConfig = DockerHostConfigFactory.Create(
                config,
                config.PortMappings is null || config.PortMappings.Count == 0,
                BuildPortBindings(config))
        };

        await EnsureImageAsync(config.Image, cancellationToken);

        var createResponse = await _client.Containers.CreateContainerAsync(createParams, cancellationToken);
        await _client.Containers.StartContainerAsync(createResponse.ID, null, cancellationToken);

        var inspect = await _client.Containers.InspectContainerAsync(createResponse.ID, cancellationToken);

        return new DockerContainerMetadata(
            ContainerId: createResponse.ID,
            Image: config.Image,
            Status: inspect.State.Status,
            Ports: ReadPublishedPorts(inspect)
        );
    }

    private static IDictionary<string, EmptyStruct>? BuildExposedPorts(ContainerConfig config)
    {
        if (config.PortMappings is null || config.PortMappings.Count == 0) return null;
        return config.PortMappings.Keys.ToDictionary(port => $"{port}/tcp", _ => new EmptyStruct());
    }

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
    }
}
