using System.Text.Json;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Koh.Configuration;

namespace NoCTF.GameModes.Registration;

public sealed class ChallengeRuntimeTemplateCatalog : IChallengeRuntimeTemplateCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ChallengeRuntimeTemplate? Get(GameMode mode, string challengeConfigurationJson) => mode switch
    {
        GameMode.Ctf => Parse<CtfChallengeConfiguration>(challengeConfigurationJson).Runtime,
        GameMode.Awd => Parse<AwdChallengeConfiguration>(challengeConfigurationJson).Runtime,
        GameMode.Awdp => Parse<AwdpChallengeConfiguration>(challengeConfigurationJson).Runtime,
        GameMode.Koh => Parse<KohChallengeConfiguration>(challengeConfigurationJson).Runtime,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported game mode.")
    };

    private static T Parse<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new GameModeConfigurationException($"{typeof(T).Name} is required.");
}

internal static class ChallengeRuntimeTemplateValidator
{
    public static IReadOnlyList<string> Validate(
        ChallengeRuntimeTemplate? runtime,
        bool allowControlCheckUrlBinding = false,
        RuntimeInternalEndpointBinding? internalEndpointBinding = null)
    {
        if (runtime is null) return [];
        var errors = new List<string>();
        if (!allowControlCheckUrlBinding && runtime.ControlCheckUrlBinding is not null)
            errors.Add("ControlCheckUrlBinding is only supported for KoH runtimes.");
        if (!Enum.IsDefined(runtime.Allocation)) errors.Add("Runtime allocation is invalid.");
        if (!Enum.IsDefined(runtime.FlagSource)) errors.Add("Runtime flag source is invalid.");
        if (runtime.Definition is null)
            errors.Add("Runtime definition is required.");
        var hasReservedEnvironmentVariable = false;
        var environment = runtime.Definition switch
        {
            ContainerRuntimeDefinition container => container.Environment,
            ComposeRuntimeDefinition compose => compose.Environment,
            _ => null
        };
        foreach (var variable in environment ?? new Dictionary<string, string>())
        {
            if (!IsEnvironmentVariableName(variable.Key))
                errors.Add($"Runtime environment variable '{variable.Key}' is invalid.");
            if (variable.Key.StartsWith("NOCTF_", StringComparison.OrdinalIgnoreCase))
                hasReservedEnvironmentVariable = true;
        }
        if (hasReservedEnvironmentVariable)
            errors.Add("Runtime environment variables cannot use the NOCTF_ prefix.");
        ValidateFlagEnvironment(runtime, errors);
        switch (runtime.Definition)
        {
            case ContainerRuntimeDefinition container:
                ValidateEgressPolicy(container.EgressPolicy, errors);
                if (string.IsNullOrWhiteSpace(container.Image))
                    errors.Add("Runtime image is required.");
                else if (container.Image.Length > 512)
                    errors.Add("Runtime image cannot exceed 512 characters.");
                foreach (var port in container.PortMappings ?? new Dictionary<int, int>())
                {
                    if (port.Key is < 1 or > 65535)
                        errors.Add($"Runtime container port {port.Key} is invalid.");
                    if (port.Value != 0)
                        errors.Add(
                            $"Runtime host port for container port {port.Key} must be 0 because the platform allocates published ports.");
                }
                if (container.InternalPorts?.Any(port => port is < 1 or > 65535) == true)
                    errors.Add("Runtime internal ports must be between 1 and 65535.");
                if (container.InternalPorts?.Distinct().Count() != container.InternalPorts?.Count)
                    errors.Add("Runtime internal ports cannot contain duplicates.");
                if (container.Security is { } security)
                {
                    if (!security.RunAsNonRoot)
                        errors.Add("Runtime security must require a non-root user.");
                    if (!security.NoNewPrivileges)
                        errors.Add("Runtime security must disable privilege escalation.");
                    if (security.CapDrop is null
                        || !security.CapDrop.Contains("ALL", StringComparer.OrdinalIgnoreCase))
                        errors.Add("Runtime security must drop all capabilities.");
                }
                break;
            case ComposeRuntimeDefinition compose:
                ValidateEgressPolicy(compose.EgressPolicy, errors);
                errors.AddRange(ComposeRuntimeDefinitionPolicy.Validate(
                    compose,
                    runtime.Limits ?? new(long.MaxValue, long.MaxValue, long.MaxValue),
                    runtime.UrlBindings,
                    runtime.ControlCheckUrlBinding,
                    requireServicePids: true,
                    requireDnsServiceNames: true,
                    internalEndpointBinding: internalEndpointBinding));
                break;
            case OvaRuntimeDefinition:
                errors.Add("Challenge definitions support only portable Container or Compose runtimes.");
                break;
        }
        if (runtime.TtlSeconds is <= 0 or > 604800)
            errors.Add("Runtime TtlSeconds must be between 1 and 604800 when configured.");
        if (runtime.OperationTimeoutSeconds is <= 0 or > 300)
            errors.Add("Runtime OperationTimeoutSeconds must be between 1 and 300 when configured.");
        if (runtime.Limits is not { } limits)
            errors.Add("Runtime resource limits are required.");
        else if (limits.MemoryBytes <= 0
            || limits.NanoCpus <= 0
            || limits.PidsLimit <= 0)
            errors.Add("Runtime resource limits must be positive.");
        if (runtime.UrlBindings?.Any(binding => binding is null) == true)
            errors.Add("Runtime URL bindings cannot contain null entries.");
        var urlBindings = (runtime.UrlBindings ?? [])
            .Append(runtime.ControlCheckUrlBinding)
            .Where(binding => binding is not null)
            .Select(binding => binding!)
            .ToArray();
        foreach (var binding in urlBindings)
        {
            if (!Enum.IsDefined(binding.Exposure) || string.IsNullOrWhiteSpace(binding.UrlTemplate))
                errors.Add("Runtime URL bindings require a valid exposure and template.");
            if (!string.IsNullOrWhiteSpace(binding.UrlTemplate))
            {
                var remainingTemplate = binding.UrlTemplate
                    .Replace("{HOST}", string.Empty, StringComparison.Ordinal)
                    .Replace("{PORT}", string.Empty, StringComparison.Ordinal);
                if (remainingTemplate.Contains('{') || remainingTemplate.Contains('}'))
                    errors.Add("Runtime URL bindings only allow HOST and PORT placeholders.");
                var expandedTemplate = binding.UrlTemplate
                    .Replace("{HOST}", "runtime.invalid", StringComparison.Ordinal)
                    .Replace("{PORT}", "1", StringComparison.Ordinal);
                if (!Uri.TryCreate(expandedTemplate, UriKind.Absolute, out _))
                    errors.Add("Runtime URL bindings must expand to an absolute URI.");
            }
            if (binding.ContainerPort is < 1 or > 65535
                || binding.GuestPort is < 1 or > 65535)
                errors.Add("Runtime URL binding ports must be between 1 and 65535.");
            switch (runtime.Definition?.RuntimeKind)
            {
                case RuntimeKind.Container:
                    if (binding.ContainerPort is null)
                        errors.Add("Container URL bindings require ContainerPort.");
                    if (binding.ServiceName is not null
                        || binding.VmId is not null
                        || binding.GuestPort is not null)
                        errors.Add(
                            "Container URL bindings cannot specify ServiceName, VmId, or GuestPort.");
                    break;
                case RuntimeKind.Compose:
                    if (string.IsNullOrWhiteSpace(binding.ServiceName)
                        || binding.ContainerPort is null)
                        errors.Add("Compose URL bindings require ServiceName and ContainerPort.");
                    if (binding.VmId is not null || binding.GuestPort is not null)
                        errors.Add("Compose URL bindings cannot specify VmId or GuestPort.");
                    break;
                case RuntimeKind.OvaVm:
                    if (string.IsNullOrWhiteSpace(binding.VmId))
                        errors.Add("OVA URL bindings require VmId.");
                    if (binding.ContainerPort is not null || binding.ServiceName is not null)
                        errors.Add(
                            "OVA URL bindings cannot specify ContainerPort or ServiceName.");
                    if (binding.GuestPort is null
                        && binding.UrlTemplate?.Contains(
                            "{PORT}",
                            StringComparison.Ordinal) == true)
                        errors.Add("OVA URL bindings cannot use PORT without GuestPort.");
                    break;
            }
        }
        if (runtime.Definition is ContainerRuntimeDefinition containerDefinition)
        {
            foreach (var binding in urlBindings)
            {
                if (binding.ContainerPort is not int containerPort)
                    continue;
                if (!(containerDefinition.PortMappings ?? new Dictionary<int, int>())
                    .TryGetValue(containerPort, out var hostPort)
                    || hostPort != 0)
                    errors.Add("Container URL bindings require a dynamic port mapping.");
            }
        }
        return errors;
    }

    private static void ValidateEgressPolicy(
        RuntimeEgressPolicy egressPolicy,
        ICollection<string> errors)
    {
        if (!Enum.IsDefined(egressPolicy))
        {
            errors.Add("Runtime egress policy is invalid.");
            return;
        }
        if (egressPolicy == RuntimeEgressPolicy.InternetOnly)
        {
            errors.Add(
                "Portable challenge runtimes cannot require the Kubernetes-only InternetOnly egress policy.");
        }
    }

    private static void ValidateFlagEnvironment(
        ChallengeRuntimeTemplate runtime,
        ICollection<string> errors)
    {
        switch (runtime.Definition)
        {
            case ContainerRuntimeDefinition container:
                if (runtime.FlagSource == RuntimeFlagSource.PerTeam
                    && string.IsNullOrWhiteSpace(container.FlagEnvironmentVariableName))
                {
                    errors.Add(
                        "PerTeam Container runtimes require FlagEnvironmentVariableName.");
                }
                if (runtime.FlagSource != RuntimeFlagSource.PerTeam
                    && container.FlagEnvironmentVariableName is not null)
                {
                    errors.Add(
                        "FlagEnvironmentVariableName is only supported for PerTeam runtimes.");
                }
                ValidateFlagEnvironmentVariableName(
                    container.FlagEnvironmentVariableName,
                    errors);
                break;
            case ComposeRuntimeDefinition compose:
                var targets = compose.FlagEnvironmentVariables
                    ?? new Dictionary<string, string>();
                if (runtime.FlagSource == RuntimeFlagSource.PerTeam && targets.Count == 0)
                {
                    errors.Add(
                        "PerTeam Compose runtimes require FlagEnvironmentVariables.");
                }
                if (runtime.FlagSource != RuntimeFlagSource.PerTeam && targets.Count > 0)
                {
                    errors.Add(
                        "FlagEnvironmentVariables are only supported for PerTeam runtimes.");
                }
                foreach (var target in targets)
                {
                    if (!compose.ServiceResources.ContainsKey(target.Key))
                    {
                        errors.Add(
                            $"Flag environment target service '{target.Key}' is not defined.");
                    }
                    ValidateFlagEnvironmentVariableName(target.Value, errors);
                }
                break;
        }
    }

    private static void ValidateFlagEnvironmentVariableName(
        string? name,
        ICollection<string> errors)
    {
        if (name is null)
            return;
        if (!IsEnvironmentVariableName(name))
            errors.Add($"Flag environment variable '{name}' is invalid.");
        if (name.StartsWith("NOCTF_", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Flag environment variables cannot use the NOCTF_ prefix.");
        }
    }

    private static bool IsEnvironmentVariableName(string name)
    {
        if (string.IsNullOrEmpty(name)
            || !(IsAsciiLetter(name[0]) || name[0] == '_'))
            return false;
        return name.Skip(1).All(character =>
            IsAsciiLetter(character) || char.IsAsciiDigit(character) || character == '_');
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
