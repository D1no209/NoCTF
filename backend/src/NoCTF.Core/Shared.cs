namespace NoCTF.Core;

public interface ITenantEntity
{
    Guid CompetitionId { get; set; }
}

public record PointsConfig(
    int InitialPoints = 1000,
    int MinimumPoints = 100,
    int DecayFactor = 450,
    string DecayFunction = "logarithmic"
);

public record PortMapping(int ContainerPort, int HostPort);
