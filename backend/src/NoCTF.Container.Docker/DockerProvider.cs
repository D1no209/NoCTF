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
            Cmd = config.Command?.Split(' ') ?? null,
            Env = config.EnvironmentVariables?.Select(kvp => $"{kvp.Key}={kvp.Value}").ToList() ?? [],
            Labels = config.Labels ?? new Dictionary<string, string>(),
            HostConfig = DockerHostConfigFactory.Create(
                config,
                config.PortMappings is null || config.PortMappings.Count == 0,
                config.PortMappings?.ToDictionary(
                    kvp => $"{kvp.Key}/tcp",
                    kvp => (IList<PortBinding>)new List<PortBinding> { new() { HostPort = kvp.Value == 0 ? "0" : kvp.Value.ToString() } }
                ) ?? new Dictionary<string, IList<PortBinding>>())
        };

        // Ensure image exists (pull if needed)
        await _client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = config.Image },
            null,
            new Progress<JSONMessage>(),
            cancellationToken);

        var createResponse = await _client.Containers.CreateContainerAsync(createParams, cancellationToken);
        await _client.Containers.StartContainerAsync(createResponse.ID, null, cancellationToken);

        var inspect = await _client.Containers.InspectContainerAsync(createResponse.ID, cancellationToken);

        return new DockerContainerMetadata(
            ContainerId: createResponse.ID,
            Image: config.Image,
            Status: inspect.State.Status,
            Ports: inspect.NetworkSettings.Ports?.ToDictionary(
                kvp => int.Parse(kvp.Key.Split('/')[0]),
                kvp => int.Parse(kvp.Value?.FirstOrDefault()?.HostPort ?? "0")
            ) ?? new Dictionary<int, int>()
        );
    }

    public async Task DestroyContainerAsync(DockerContainerMetadata metadata, CancellationToken cancellationToken = default)
    {
        await _client.Containers.RemoveContainerAsync(
            metadata.ContainerId,
            new ContainerRemoveParameters { Force = true },
            cancellationToken);
    }
}
