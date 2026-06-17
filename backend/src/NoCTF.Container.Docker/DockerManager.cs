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
            ExpectedStopAt: config.Ttl.HasValue ? DateTime.UtcNow.Add(config.Ttl.Value) : null
        );
    }

    public async Task DestroyContainerAsync(ContainerInstance container, CancellationToken cancellationToken = default)
    {
        var metadata = new DockerContainerMetadata(
            container.ContainerId,
            "",
            container.Status,
            container.PortMappings
        );
        await provider.DestroyContainerAsync(metadata, cancellationToken);
    }

    public async Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
    {
        var client = provider.CreateClient();

        var createParams = new CreateContainerParameters
        {
            Image = config.Image,
            Cmd = config.Command?.Split(' ') ?? null,
            Env = config.EnvironmentVariables?.Select(kvp => $"{kvp.Key}={kvp.Value}").ToList() ?? [],
            Labels = config.Labels ?? new Dictionary<string, string>(),
            HostConfig = DockerHostConfigFactory.Create(config, publishAllPorts: false)
        };

        // Pull image if needed
        await client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = config.Image },
            null,
            new Progress<JSONMessage>(),
            cancellationToken);

        var startedAt = DateTime.UtcNow;
        var createResponse = await client.Containers.CreateContainerAsync(createParams, cancellationToken);
        var containerId = createResponse.ID;

        await client.Containers.StartContainerAsync(containerId, null, cancellationToken);

        // Wait for container to exit
        var waitResponse = await client.Containers.WaitContainerAsync(containerId, cancellationToken);

        var finishedAt = DateTime.UtcNow;

        // Collect logs
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
            // Log collection is best-effort
        }

        // Remove container
        try
        {
            await client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
        catch
        {
            // Removal is best-effort
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
}
