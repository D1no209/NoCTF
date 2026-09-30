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
            || template.Definition is not ContainerRuntimeDefinition { Services.Count: 1 })
        {
            throw new InvalidOperationException(
                "Patch verification targets require a Docker or Kubernetes Container runtime.");
        }
        RuntimeInstance runtime = purpose switch
        {
            RuntimePurpose.AwdpTarget => new AwdpTargetRuntimeInstance(),
            RuntimePurpose.PatchVerificationTarget => new PatchVerificationTargetRuntimeInstance(),
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
        };
        runtime.Id = runtimeInstanceId;
        runtime.CompetitionId = competitionId;
        runtime.CompetitionChallengeId = competitionChallengeId;
        runtime.TeamId = teamId;
        runtime.GameplayFactId = gameplayFactId;
        runtime.RuntimeKind = RuntimeKind.Container;
        runtime.RuntimeProvider = placement.Provider;
        runtime.State = RuntimeState.Queued;
        runtime.CreatedAt = now;
        return runtime;
    }
}

public static class PatchVerificationTargetDefinitionFactory
{
    public static ContainerRuntimeRequest Create(Guid operationId, ChallengeRuntimeTemplate template,
        RuntimeProvider provider, RuntimePurpose purpose, long processLimit = 256)
    {
        if (purpose is not (RuntimePurpose.AwdpTarget or RuntimePurpose.PatchVerificationTarget)
            || template.Definition is not ContainerRuntimeDefinition { Services.Count: 1 } definition)
            throw new InvalidOperationException("Patch verification requires exactly one Runtime service.");
        var service = definition.Services.Single();
        if (service.InternalPorts is not { Count: 1 } || service.InternalPorts[0] is < 1 or > 65535)
            throw new InvalidOperationException("Patch verification requires exactly one valid internal target port.");
        return new(operationId, provider, [service], new Dictionary<string, string>
        {
            ["noctf.io/managed"] = "true", ["noctf.io/runtime-instance-id"] = operationId.ToString("D"),
            ["noctf.io/purpose"] = purpose == RuntimePurpose.AwdpTarget ? "awdp-target" : "patch-verification-target"
        }, service.Resources(processLimit), TimeSpan.FromSeconds(template.TtlSeconds ?? 900),
            TimeSpan.FromSeconds(template.OperationTimeoutSeconds ?? 120), EgressPolicy: definition.EgressPolicy,
            Purpose: ContainerNetworkPurpose.AwdpVerification);
    }

}
