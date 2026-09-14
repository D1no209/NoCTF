using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.GameModes.PatchVerification.Runtime;

public static class PatchVerificationTargetRuntimeFactory
{
    public static RuntimeInstance Create(
        Guid teamId,
        Guid? gameplayFactId,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeInstanceId,
        ChallengeRuntimeTemplate template,
        RuntimePlacement placement,
        RuntimePurpose purpose,
        DateTimeOffset now)
    {
        if (purpose is not (RuntimePurpose.AwdpTarget or RuntimePurpose.PatchVerificationTarget)
            || template.Definition is not ContainerRuntimeDefinition)
        {
            throw new InvalidOperationException(
                "Patch verification targets require a Docker or Kubernetes Container runtime.");
        }
        return new RuntimeInstance
        {
            Id = runtimeInstanceId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            Purpose = purpose,
            GameplayFactId = gameplayFactId,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = placement.Provider,
            State = RuntimeState.Queued,
            CreatedAt = now
        };
    }
}

public static class PatchVerificationTargetDefinitionFactory
{
    public static ContainerRequest Create(
        Guid operationId,
        ChallengeRuntimeTemplate template,
        RuntimeProvider provider,
        RuntimePurpose purpose)
    {
        if (purpose is not (RuntimePurpose.AwdpTarget or RuntimePurpose.PatchVerificationTarget)
            || template.Definition is not ContainerRuntimeDefinition definition)
        {
            throw new InvalidOperationException(
                "Patch verification targets require a Docker or Kubernetes Container runtime.");
        }
        if (definition.InternalPorts is not { Count: 1 }
            || definition.InternalPorts[0] is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                "Patch verification target Runtime must declare exactly one valid InternalPort.");
        }

        var ttl = template.TtlSeconds is > 0
            ? TimeSpan.FromSeconds(template.TtlSeconds.Value)
            : TimeSpan.FromMinutes(15);
        var labels = definition.Labels is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(definition.Labels, StringComparer.Ordinal);
        labels["noctf.io/managed"] = "true";
        labels["noctf.io/runtime-instance-id"] = operationId.ToString("D");
        labels["noctf.io/purpose"] = purpose == RuntimePurpose.AwdpTarget
            ? "awdp-target"
            : "patch-verification-target";
        labels["noctf.io/job-kind"] = purpose == RuntimePurpose.AwdpTarget
            ? "awdp-verification"
            : "patch-verification";
        var security = definition.Security
            ?? new ContainerSecurityPolicy(false, false, false, [], []);
        security = security with
        {
            CapDrop = security.CapDrop ?? [],
            CapAdd = security.CapAdd ?? []
        };
        return new ContainerRequest(
            operationId,
            provider,
            definition.Image,
            definition.Command ?? [],
            definition.Environment ?? new Dictionary<string, string>(),
            labels,
            new Dictionary<int, int>(),
            template.Limits ?? new RuntimeResourceLimits(512 * 1024 * 1024, 500_000_000, 256),
            security,
            Ttl: ttl,
            OperationTimeout: template.OperationTimeoutSeconds is > 0
                ? TimeSpan.FromSeconds(template.OperationTimeoutSeconds.Value)
                : TimeSpan.FromMinutes(2),
            NetworkIsolation: ContainerNetworkIsolation.Isolated,
            InternalPorts: definition.InternalPorts,
            RuntimeInstanceId: operationId,
            EgressPolicy: definition.EgressPolicy,
            NetworkPurpose: ContainerNetworkPurpose.AwdpVerification);
    }
}
