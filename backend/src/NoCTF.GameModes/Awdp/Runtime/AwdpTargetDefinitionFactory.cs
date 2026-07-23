using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.GameModes.Awdp.Runtime;

public static class AwdpTargetDefinitionFactory
{
    public static ContainerRequest Create(
        Guid operationId,
        int generation,
        ChallengeRuntimeTemplate template,
        int targetPort,
        DateTimeOffset now)
    {
        if (template.RuntimeKind != RuntimeKind.Container
            || template.Provider is not (RuntimeProvider.Docker or RuntimeProvider.Kubernetes))
            throw new InvalidOperationException(
                "AWDP disposable targets require a Docker or Kubernetes Container runtime.");
        if (targetPort is < 1 or > 65535)
            throw new InvalidOperationException("AWDP TargetPort must be between 1 and 65535.");

        var ttl = template.TtlSeconds is > 0
            ? TimeSpan.FromSeconds(template.TtlSeconds.Value)
            : TimeSpan.FromMinutes(15);
        var labels = template.Labels is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(template.Labels, StringComparer.Ordinal);
        labels["noctf.io/managed"] = "true";
        labels["noctf.io/runtime-instance-id"] = operationId.ToString("D");
        labels["noctf.io/generation"] = generation.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        labels["noctf.io/purpose"] = "awdp-target";
        labels["noctf.io/job-kind"] = "awdp-verification";
        labels["noctf.io/expires-at"] = now.Add(ttl).ToUnixTimeSeconds().ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        return new ContainerRequest(
            operationId,
            template.Provider,
            template.Image,
            template.Command ?? [],
            template.Environment ?? new Dictionary<string, string>(),
            labels,
            new Dictionary<int, int>(),
            template.Limits ?? new ContainerResourceLimits(512 * 1024 * 1024, 500_000_000, 256),
            new ContainerSecurityPolicy(true, false, true, ["ALL"], []),
            Ttl: ttl,
            OperationTimeout: template.OperationTimeoutSeconds is > 0
                ? TimeSpan.FromSeconds(template.OperationTimeoutSeconds.Value)
                : TimeSpan.FromMinutes(2),
            NetworkIsolation: ContainerNetworkIsolation.Isolated,
            InternalPorts: [targetPort],
            Generation: generation);
    }
}
