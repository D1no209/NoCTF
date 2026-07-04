namespace NoCTF.Container.Docker;

public record DockerContainerMetadata(
    string ContainerId,
    string Image,
    string Status,
    Dictionary<int, int> Ports,
    string? NetworkName = null
);
