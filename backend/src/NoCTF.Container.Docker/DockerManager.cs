using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.PluginBase;

namespace NoCTF.Container.Docker;

public class DockerManager(DockerProvider provider) : IContainerManager
{
    public async Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
    {
        var metadata = await provider.CreateContainerAsync(config, cancellationToken);

        return new ContainerInstance(
            Id: Guid.NewGuid(),
            CompetitionId: config.Labels?.TryGetValue("competitionId", out var compId) == true && Guid.TryParse(compId, out var c) ? c : Guid.Empty,
            TeamId: config.Labels?.TryGetValue("teamId", out var teamId) == true && Guid.TryParse(teamId, out var t) ? t : null,
            ChallengeId: config.Labels?.TryGetValue("challengeId", out var challId) == true && Guid.TryParse(challId, out var ch) ? ch : null,
            ProviderType: "docker",
            ContainerId: metadata.ContainerId,
            PortMappings: metadata.Ports,
            Status: metadata.Status,
            StartedAt: DateTime.UtcNow,
            ExpectedStopAt: config.Ttl.HasValue ? DateTime.UtcNow.Add(config.Ttl.Value) : null,
            OrchestrationNamespace: metadata.NetworkName
        );
    }

    public async Task DestroyContainerAsync(ContainerInstance container, CancellationToken cancellationToken = default)
    {
        var metadata = new DockerContainerMetadata(
            container.ContainerId,
            "",
            container.Status,
            container.PortMappings,
            container.OrchestrationNamespace
        );
        await provider.DestroyContainerAsync(metadata, cancellationToken);
    }

    public async Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
    {
        var client = provider.CreateClient();

        var createParams = new CreateContainerParameters
        {
            Image = config.Image,
            Cmd = string.IsNullOrWhiteSpace(config.Command)
                ? null
                : config.Entrypoint is { Count: > 0 }
                    ? [config.Command]
                    : ["/bin/sh", "-c", config.Command],
            Entrypoint = config.Entrypoint?.ToList(),
            Env = config.EnvironmentVariables?.Select(kvp => $"{kvp.Key}={kvp.Value}").ToList() ?? [],
            Labels = config.Labels ?? new Dictionary<string, string>(),
            User = DockerHostConfigFactory.ResolveUser(config),
            HostConfig = DockerHostConfigFactory.Create(config, publishAllPorts: false),
            NetworkingConfig = BuildNetworkingConfig(config)
        };

        try
        {
            await client.Images.InspectImageAsync(config.Image, cancellationToken);
        }
        catch (DockerImageNotFoundException)
        {
            await client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = config.Image },
                null,
                new Progress<JSONMessage>(),
                cancellationToken);
        }

        var startedAt = DateTime.UtcNow;
        string? containerId = null;
        try
        {
            var createResponse = await client.Containers.CreateContainerAsync(createParams, cancellationToken);
            containerId = createResponse.ID;

            await client.Containers.StartContainerAsync(containerId, null, cancellationToken);

            var waitResponse = await client.Containers.WaitContainerAsync(containerId, cancellationToken);
            var finishedAt = DateTime.UtcNow;

            string? stdOut = null;
            string? stdErr = null;
            try
            {
                var logsParams = new ContainerLogsParameters
                {
                    ShowStdout = true,
                    ShowStderr = true,
                    Tail = "100"
                };
                using var logStream = await client.Containers.GetContainerLogsAsync(containerId, false, logsParams, cancellationToken);
                (stdOut, stdErr) = await logStream.ReadOutputToEndAsync(cancellationToken);
            }
            catch
            {
                // Log collection is best-effort.
            }

            return new ContainerRunResult(
                ContainerId: containerId,
                ExitCode: (int)waitResponse.StatusCode,
                StdOut: stdOut,
                StdErr: stdErr,
                StartedAt: startedAt,
                FinishedAt: finishedAt
            );
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(containerId))
                await RemoveContainerBestEffortAsync(client, containerId);
        }
    }

    private static NetworkingConfig? BuildNetworkingConfig(ContainerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.NetworkName))
            return null;

        return new NetworkingConfig
        {
            EndpointsConfig = new Dictionary<string, EndpointSettings>
            {
                [config.NetworkName] = new()
                {
                    Aliases = config.NetworkAliases?
                        .Where(alias => !string.IsNullOrWhiteSpace(alias))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                }
            }
        };
    }

    private static async Task RemoveContainerBestEffortAsync(DockerClient client, string containerId)
    {
        try
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                cleanupCts.Token);
        }
        catch
        {
            // Removal is best-effort.
        }
    }

    public async Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken cancellationToken = default)
    {
        DockerComposeRunner.ValidateComposeYaml(config.ComposeYaml);

        await DockerComposeRunner.RunAsync(
            config.ComposeYaml,
            config.ProjectName,
            ["up", "-d", "--remove-orphans"],
            config.EnvironmentVariables,
            cancellationToken);

        return new ComposeDeployment(
            Id: Guid.NewGuid(),
            CompetitionId: config.Labels?.TryGetValue("competitionId", out var compId) == true && Guid.TryParse(compId, out var c) ? c : Guid.Empty,
            TeamId: config.Labels?.TryGetValue("teamId", out var teamId) == true && Guid.TryParse(teamId, out var t) ? t : null,
            ChallengeId: config.Labels?.TryGetValue("challengeId", out var challId) == true && Guid.TryParse(challId, out var ch) ? ch : null,
            ProviderType: "docker-compose",
            ProjectName: config.ProjectName,
            ComposeYaml: config.ComposeYaml,
            Status: "running",
            StartedAt: DateTime.UtcNow,
            ExpectedStopAt: config.Ttl.HasValue ? DateTime.UtcNow.Add(config.Ttl.Value) : null
        );
    }

    public async Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken cancellationToken = default)
    {
        await DockerComposeRunner.RunAsync(
            deployment.ComposeYaml,
            deployment.ProjectName,
            ["down", "--remove-orphans", "--volumes"],
            null,
            cancellationToken);
    }

    public async Task<ComposeStatus> GetComposeStatusAsync(
        string projectName,
        Dictionary<string, string>? labels = null,
        CancellationToken cancellationToken = default)
    {
        var client = provider.CreateClient();
        var labelFilters = new Dictionary<string, bool>
        {
            [$"com.docker.compose.project={projectName}"] = true
        };

        if (labels is not null)
        {
            foreach (var (key, value) in labels)
                labelFilters[$"{key}={value}"] = true;
        }

        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = labelFilters
                }
            },
            cancellationToken);

        var services = containers.Select(container =>
        {
            container.Labels.TryGetValue("com.docker.compose.service", out var serviceName);
            container.Labels.TryGetValue("nodeId", out var nodeIdValue);
            Guid? nodeId = Guid.TryParse(nodeIdValue, out var parsedNodeId) ? parsedNodeId : null;
            var ports = container.Ports
                .Where(p => p.PublicPort > 0 && p.PrivatePort > 0)
                .GroupBy(p => (int)p.PrivatePort)
                .ToDictionary(g => g.Key, g => (int)g.First().PublicPort);

            return new ComposeServiceInstance(
                ServiceName: serviceName ?? string.Empty,
                ContainerId: container.ID,
                Status: container.State,
                NodeId: nodeId,
                PublishedPorts: ports);
        }).ToList();

        var status = services.Count == 0
            ? "not_found"
            : services.Any(s => string.Equals(s.Status, "running", StringComparison.OrdinalIgnoreCase))
                ? "running"
                : "stopped";

        return new ComposeStatus(projectName, status, services);
    }
}
