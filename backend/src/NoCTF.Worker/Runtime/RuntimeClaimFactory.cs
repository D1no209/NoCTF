using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awd.Configuration;

namespace NoCTF.Worker.Runtime;

public static class RuntimeClaimFactory
{
    public static IRuntimeProvisionMessage Create(
        RuntimeInstance instance,
        string runnerId,
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
                when instance.RuntimeProvider is RuntimeProvider.Docker or RuntimeProvider.Kubernetes =>
                new ProvisionContainerRuntime(
                    instance.Id,
                    runnerId,
                    new ContainerRequest(
                        instance.Id,
                        instance.RuntimeProvider,
                        definition.Image,
                        definition.Command ?? [],
                        ContainerEnvironment(definition, fixedFlag),
                        MergeLabels(definition.Labels, instance),
                        ContainerPortMappings(
                            instance.RuntimeProvider,
                            definition.PortMappings,
                            instance.AccessMode),
                        limits,
                        NormalizeSecurity(definition.Security),
                        ttl,
                        OperationTimeout: operationTimeout,
                        NetworkIsolation: ContainerNetworkIsolation.Isolated,
                        InternalPorts: InternalPorts(
                            mode,
                            template,
                            definition,
                            instance.AccessMode),
                        AllowInternalCallback: mode == GameMode.Koh,
                        RuntimeInstanceId: instance.Id,
                        UrlBindings: template.UrlBindings,
                        ControlCheckUrlBinding: mode == GameMode.Koh
                            ? template.ControlCheckUrlBinding
                            : null,
                        AwdCheckerTargetBinding: checkerTarget,
                        EgressPolicy: definition.EgressPolicy,
                        AccessMode: instance.AccessMode)),
            ComposeRuntimeDefinition definition
                when instance.RuntimeProvider is RuntimeProvider.Docker or RuntimeProvider.Kubernetes =>
                new ProvisionComposeRuntime(
                    instance.Id,
                    runnerId,
                    new ComposeRequest(
                        instance.Id,
                        instance.RuntimeProvider,
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
                        definition.EgressPolicy,
                        AccessMode: instance.AccessMode)),
            OvaRuntimeDefinition definition when instance.RuntimeProvider == RuntimeProvider.Libvirt =>
                new ProvisionOvaRuntime(
                    instance.Id,
                    runnerId,
                    new OvaRuntimeRequest(
                        instance.Id,
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
        var checker = AwdConfigurationUpgrader.ParseChallenge(
            challengeConfigurationJson).Checker;
        if (checker is null)
            return null;
        return checker.TargetServiceName switch
        {
            { Length: > 0 } serviceName => new(serviceName),
            _ => new()
        };
    }

    private static IReadOnlyList<int>? InternalPorts(
        GameMode mode,
        ChallengeRuntimeTemplate template,
        ContainerRuntimeDefinition definition,
        RuntimeAccessMode accessMode)
    {
        var ports = new List<int>(definition.InternalPorts ?? []);
        if (accessMode is RuntimeAccessMode.DirectAndWsrx or RuntimeAccessMode.WsrxOnly)
        {
            ports.AddRange((template.UrlBindings ?? [])
                .Select(binding => binding.ContainerPort)
                .OfType<int>());
        }
        if (mode == GameMode.Koh
            && template.ControlCheckUrlBinding?.ContainerPort is int controlPort)
            ports.Add(controlPort);
        return ports.Count == 0 ? null : ports.Distinct().Order().ToArray();
    }

    private static IReadOnlyDictionary<int, int> ContainerPortMappings(
        RuntimeProvider provider,
        IReadOnlyDictionary<int, int>? configured,
        RuntimeAccessMode accessMode)
    {
        if (configured is null || configured.Count == 0
            || accessMode == RuntimeAccessMode.WsrxOnly)
            return new Dictionary<int, int>();
        return provider == RuntimeProvider.Docker
            ? configured.Keys.ToDictionary(port => port, _ => 0)
            : configured;
    }

    private static ContainerSecurityPolicy NormalizeSecurity(
        ContainerSecurityPolicy? security)
    {
        var configured = security
            ?? new ContainerSecurityPolicy(false, false, false, [], []);
        return configured with
        {
            CapDrop = configured.CapDrop ?? [],
            CapAdd = configured.CapAdd ?? []
        };
    }

    private static string? ResolvePerTeamFlag(
        GameMode mode,
        ChallengeRuntimeTemplate template,
        string? perTeamFlag)
    {
        if (mode is not (GameMode.Ctf or GameMode.Awdp)
            || template.FlagSource != RuntimeFlagSource.PerTeam)
            return null;
        return !string.IsNullOrEmpty(perTeamFlag)
            ? perTeamFlag
            : throw new InvalidOperationException(
                "A PerTeam runtime requires its fixed team flag.");
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
        $"noctf-{instance.Id:N}";

    private static IReadOnlyDictionary<string, string> MergeLabels(
        IReadOnlyDictionary<string, string>? configured,
        RuntimeInstance instance)
    {
        var labels = configured is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(configured, StringComparer.Ordinal);
        labels["noctf.io/managed"] = "true";
        labels["noctf.io/job-kind"] = instance.Purpose == RuntimePurpose.TemplateTest
            ? "challenge-test-runtime"
            : "persistent-runtime";
        labels["noctf.io/runtime-instance-id"] = instance.Id.ToString("D");
        if (instance.CompetitionId is Guid competitionId)
            labels["noctf.io/competition-id"] = competitionId.ToString("D");
        if (instance.CompetitionChallengeId is Guid competitionChallengeId)
            labels["noctf.io/competition-challenge-id"] = competitionChallengeId.ToString("D");
        if (instance.ChallengeId is Guid challengeId)
            labels["noctf.io/challenge-id"] = challengeId.ToString("D");
        if (instance.TeamId is Guid teamId)
            labels["noctf.io/team-id"] = teamId.ToString("D");
        if (instance.AccessMode is RuntimeAccessMode.DirectAndWsrx
            or RuntimeAccessMode.WsrxOnly)
        {
            labels["noctf.io/runtime-proxy-target"] = "true";
        }
        return labels;
    }
}
