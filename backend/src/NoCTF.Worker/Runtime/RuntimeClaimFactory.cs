using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awd.Configuration;

namespace NoCTF.Worker.Runtime;

public static class RuntimeClaimFactory
{
    public static IRunnerPoolMessage Create(
        RuntimeInstance instance,
        GameMode mode,
        ChallengeRuntimeTemplate template,
        string challengeConfigurationJson,
        string? perTeamFlag = null)
    {
        var fixedFlag = ResolvePerTeamFlag(mode, template, perTeamFlag);
        var checkerTarget = ResolveAwdCheckerTarget(mode, challengeConfigurationJson);
        var limits = template.Limits
            ?? new RuntimeResourceLimits(512 * 1024 * 1024, 500_000_000, 256);
        TimeSpan? ttl = template.TtlSeconds is > 0
            ? TimeSpan.FromSeconds(template.TtlSeconds.Value)
            : null;
        var operationTimeout = template.OperationTimeoutSeconds is > 0
            ? TimeSpan.FromSeconds(template.OperationTimeoutSeconds.Value)
            : TimeSpan.FromMinutes(2);
        return template.Definition switch
        {
            ContainerRuntimeDefinition definition
                when template.Provider is RuntimeProvider.Docker or RuntimeProvider.Kubernetes =>
                new ClaimContainerRuntime(
                    instance.Id,
                    instance.ProcessingVersion,
                    instance.Generation,
                    instance.RunnerPool,
                    new ContainerRequest(
                        instance.Id,
                        template.Provider,
                        definition.Image,
                        definition.Command ?? [],
                        ContainerEnvironment(definition, fixedFlag),
                        MergeLabels(definition.Labels, instance),
                        definition.PortMappings ?? new Dictionary<int, int>(),
                        limits,
                        definition.Security
                            ?? new ContainerSecurityPolicy(true, true, true, ["ALL"], []),
                        ttl,
                        OperationTimeout: operationTimeout,
                        NetworkIsolation: ContainerNetworkIsolation.Isolated,
                        InternalPorts: InternalPorts(mode, template, checkerTarget),
                        Generation: instance.Generation,
                        RuntimeInstanceId: instance.Id,
                        UrlBindings: template.UrlBindings,
                        ControlCheckUrlBinding: mode == GameMode.Koh
                            ? template.ControlCheckUrlBinding
                            : null,
                        AwdCheckerTargetBinding: checkerTarget,
                        EgressPolicy: definition.EgressPolicy)),
            ComposeRuntimeDefinition definition
                when template.Provider is RuntimeProvider.Docker or RuntimeProvider.Kubernetes =>
                new ClaimComposeRuntime(
                    instance.Id,
                    instance.ProcessingVersion,
                    instance.Generation,
                    instance.RunnerPool,
                    new ComposeRequest(
                        instance.Id,
                        template.Provider,
                        instance.Generation,
                        ResourceName(instance),
                        definition.ComposeYaml,
                        definition.Environment ?? new Dictionary<string, string>(),
                        MergeLabels(definition.Labels, instance),
                        definition.ServiceResources,
                        limits,
                        ttl,
                        operationTimeout,
                        template.UrlBindings,
                        mode == GameMode.Koh ? template.ControlCheckUrlBinding : null,
                        checkerTarget,
                        ServiceEnvironment(definition, fixedFlag),
                        definition.EgressPolicy)),
            OvaRuntimeDefinition definition when template.Provider == RuntimeProvider.Libvirt =>
                new ClaimOvaRuntime(
                    instance.Id,
                    instance.ProcessingVersion,
                    instance.Generation,
                    instance.RunnerPool,
                    new OvaRuntimeRequest(
                        instance.Id,
                        instance.Generation,
                        ParseOvaSource(definition.OvaSourceUrl),
                        definition.Sha256.ToLowerInvariant(),
                        ResourceName(instance),
                        limits,
                        ttl,
                        operationTimeout,
                        template.UrlBindings,
                        mode == GameMode.Koh
                            ? template.ControlCheckUrlBinding
                            : null)),
            _ => throw new InvalidOperationException(
                "Runtime definition and provider are incompatible.")
        };
    }

    private static RuntimeInternalEndpointBinding? ResolveAwdCheckerTarget(
        GameMode mode,
        string challengeConfigurationJson)
    {
        if (mode != GameMode.Awd)
            return null;
        var target = AwdConfigurationUpgrader.ParseChallenge(
            challengeConfigurationJson).Checker?.Target;
        return target switch
        {
            ContainerAwdCheckerTarget container => new(
                container.UrlTemplate,
                container.ContainerPort),
            ComposeAwdCheckerTarget compose => new(
                compose.UrlTemplate,
                compose.ContainerPort,
                compose.ServiceName),
            null => null,
            _ => throw new InvalidOperationException("Unsupported AWD Checker target kind.")
        };
    }

    private static IReadOnlyList<int>? InternalPorts(
        GameMode mode,
        ChallengeRuntimeTemplate template,
        RuntimeInternalEndpointBinding? checkerTarget)
    {
        var ports = new List<int>();
        if (mode == GameMode.Koh
            && template.ControlCheckUrlBinding?.ContainerPort is int controlPort)
            ports.Add(controlPort);
        if (checkerTarget is { ServiceName: null })
            ports.Add(checkerTarget.ContainerPort);
        return ports.Count == 0 ? null : ports.Distinct().Order().ToArray();
    }

    private static string? ResolvePerTeamFlag(
        GameMode mode,
        ChallengeRuntimeTemplate template,
        string? perTeamFlag)
    {
        if (mode != GameMode.Ctf || template.FlagSource != RuntimeFlagSource.PerTeam)
            return null;
        return !string.IsNullOrEmpty(perTeamFlag)
            ? perTeamFlag
            : throw new InvalidOperationException(
                "A CTF PerTeam runtime requires its fixed team flag.");
    }

    private static IReadOnlyDictionary<string, string> ContainerEnvironment(
        ContainerRuntimeDefinition definition,
        string? fixedFlag)
    {
        var environment = definition.Environment is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(definition.Environment, StringComparer.Ordinal);
        if (fixedFlag is not null)
        {
            var variable = definition.FlagEnvironmentVariableName
                ?? throw new InvalidOperationException(
                    "A PerTeam Container runtime requires a flag environment variable.");
            environment[variable] = fixedFlag;
        }
        return environment;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>?
        ServiceEnvironment(
            ComposeRuntimeDefinition definition,
            string? fixedFlag)
    {
        if (fixedFlag is null)
            return null;
        var targets = definition.FlagEnvironmentVariables;
        if (targets is null || targets.Count == 0)
        {
            throw new InvalidOperationException(
                "A PerTeam Compose runtime requires flag environment variables.");
        }
        return targets.ToDictionary(
            target => target.Key,
            target => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                [target.Value] = fixedFlag
            },
            StringComparer.Ordinal);
    }

    private static Uri ParseOvaSource(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("OVA source must be an absolute URI.");
        return uri;
    }

    private static string ResourceName(RuntimeInstance instance) =>
        $"noctf-{instance.Id:N}-{instance.Generation}";

    private static IReadOnlyDictionary<string, string> MergeLabels(
        IReadOnlyDictionary<string, string>? configured,
        RuntimeInstance instance)
    {
        var labels = configured is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(configured, StringComparer.Ordinal);
        labels["noctf.io/managed"] = "true";
        labels["noctf.io/job-kind"] = "persistent-runtime";
        labels["noctf.io/runtime-instance-id"] = instance.Id.ToString("D");
        labels["noctf.io/competition-id"] = instance.CompetitionId.ToString("D");
        labels["noctf.io/competition-challenge-id"] =
            instance.CompetitionChallengeId.ToString("D");
        labels["noctf.io/generation"] = instance.Generation.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        if (instance.TeamId is Guid teamId)
            labels["noctf.io/team-id"] = teamId.ToString("D");
        return labels;
    }
}
