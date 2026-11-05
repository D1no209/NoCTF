using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Challenges.Capabilities;

public static class FixTargetRuntimeFactory
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
        if (purpose is not (RuntimePurpose.FixTarget or RuntimePurpose.ScopedFixTarget)
            || template.Definition is not ContainerRuntimeDefinition { Services.Count: > 0 and <= 64 })
        {
            throw new InvalidOperationException(
                "Fix verification targets require a Docker or Kubernetes Container runtime.");
        }
        RuntimeInstance runtime = purpose switch
        {
            RuntimePurpose.FixTarget => new FixTargetRuntimeInstance(),
            RuntimePurpose.ScopedFixTarget => new ScopedFixTargetRuntimeInstance(),
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

public static class FixTargetDefinitionFactory
{
    public static ContainerRuntimeRequest Create(Guid operationId, ChallengeRuntimeTemplate template,
        RuntimeProvider provider, RuntimePurpose purpose, long processLimit = 256)
    {
        if (purpose is not (RuntimePurpose.FixTarget or RuntimePurpose.ScopedFixTarget)
            || template.Definition is not ContainerRuntimeDefinition { Services.Count: > 0 and <= 64 } definition)
            throw new InvalidOperationException("Fix verification requires named Runtime services.");
        var ports = definition.Services.SelectMany(service => service.InternalPorts ?? []).ToArray();
        if (ports.Length == 0 || ports.Any(port => port is < 1 or > 65535))
            throw new InvalidOperationException("Fix verification requires valid internal service ports.");
        return new(operationId, provider, definition.Services, new Dictionary<string, string>
        {
            ["noctf.io/managed"] = "true", ["noctf.io/runtime-instance-id"] = operationId.ToString("D"),
            ["noctf.io/purpose"] = purpose == RuntimePurpose.FixTarget ? "fix-target" : "scoped-fix-target"
        }, NoCTF.Application.Runtime.Capacity.RuntimeResourceBudgetPolicy.Sum(definition.Services.Select(service => service.Resources(processLimit))), TimeSpan.FromSeconds(template.TtlSeconds ?? 900),
            TimeSpan.FromSeconds(template.OperationTimeoutSeconds ?? 120), EgressPolicy: definition.EgressPolicy,
            Purpose: ContainerNetworkPurpose.AwdpVerification);
    }

}
