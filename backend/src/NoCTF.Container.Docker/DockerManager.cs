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
}
