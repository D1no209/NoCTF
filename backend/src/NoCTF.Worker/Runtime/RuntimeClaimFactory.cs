using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Runtime;

namespace NoCTF.Worker.Runtime;

public static class RuntimeClaimFactory
{
    public static IRuntimeProvisionMessage Create(RuntimeInstance instance, string runnerId, GameMode mode,
        ChallengeRuntimeTemplate template, ChallengeDefinition? challengeDefinition, string? perTeamFlag = null, long processLimit = 256)
    {
        var ttl = template.TtlSeconds is > 0 ? TimeSpan.FromSeconds(template.TtlSeconds.Value) : (TimeSpan?)null;
        var timeout = TimeSpan.FromSeconds(template.OperationTimeoutSeconds ?? 120);
        var labels = new Dictionary<string, string>
        {
            ["noctf.io/managed"] = "true", ["noctf.io/runtime-instance-id"] = instance.Id.ToString("D"),
            ["noctf.io/purpose"] = instance.Purpose.ToString(), ["noctf.io/job-kind"] = "persistent-runtime"
        };
        if (instance.CompetitionId is Guid competitionId) labels["noctf.io/competition-id"] = competitionId.ToString("D");
        if (instance.CompetitionChallengeId is Guid competitionChallengeId) labels["noctf.io/competition-challenge-id"] = competitionChallengeId.ToString("D");
        if (instance.ChallengeId is Guid challengeId) labels["noctf.io/challenge-id"] = challengeId.ToString("D");
        if (instance.TeamId is Guid teamId) labels["noctf.io/team-id"] = teamId.ToString("D");
        if (instance.Purpose == RuntimePurpose.TemplateTest) labels["noctf.io/job-kind"] = "challenge-test-runtime";
        if (instance.AccessMode != RuntimeAccessMode.Direct) labels["noctf.io/runtime-proxy-target"] = "true";
        if (template.Definition is ContainerRuntimeDefinition container
            && instance.RuntimeProvider is RuntimeProvider.Docker or RuntimeProvider.Kubernetes)
        {
            var needsFlag = mode is GameMode.Ctf or GameMode.Awdp or GameMode.LiveSolo && template.FlagSource == RuntimeFlagSource.PerTeam;
            if (needsFlag && string.IsNullOrEmpty(perTeamFlag)) throw new RuntimeConfigurationException("PerTeam Runtime requires its fixed team Flag.");
            var services = container.Services.Select(service =>
            {
                var environment = new Dictionary<string, string>(service.Environment ?? new Dictionary<string, string>(), StringComparer.Ordinal);
                if (needsFlag && service.FlagEnvironmentVariableName is { } variable) environment[variable] = perTeamFlag!;
                return service with { Environment = environment };
            }).ToArray();
            return new ProvisionContainerRuntime(instance.Id, runnerId, new ContainerRuntimeRequest(instance.Id,
                instance.RuntimeProvider, services, labels, RuntimeResourceBudgetPolicy.Sum(services.Select(service => service.Resources(processLimit))),
                instance.ExecutionScopeId is null ? ttl : null, timeout, template.UrlBindings, mode == GameMode.Koh ? template.ControlCheckUrlBinding : null,
                mode == GameMode.Awd && challengeDefinition?.Checker is { } checker ? new(checker.TargetServiceName) : null,
                container.EgressPolicy, instance.AccessMode, ExecutionScopeId: instance.ExecutionScopeId));
        }
        if (template.Definition is OvaRuntimeDefinition ova && instance.RuntimeProvider == RuntimeProvider.Libvirt)
            return new ProvisionOvaRuntime(instance.Id, runnerId, new OvaRuntimeRequest(instance.Id,
                new Uri(ova.OvaSourceUrl, UriKind.Absolute), ova.Sha256.ToLowerInvariant(), $"noctf-rt-{instance.Id:N}",
                template.Limits ?? new(512 * 1024 * 1024, 500, processLimit), ttl, timeout, template.UrlBindings,
                mode == GameMode.Koh ? template.ControlCheckUrlBinding : null));
        throw new RuntimeConfigurationException("Runtime definition and provider are incompatible.");
    }
}
