using System.Text.Json;
using NoCTF.Application.Runtime.Ports;
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
        bool allowControlCheckUrlBinding = false)
    {
        if (runtime is null) return [];
        var errors = new List<string>();
        if (!allowControlCheckUrlBinding && runtime.ControlCheckUrlBinding is not null)
            errors.Add("ControlCheckUrlBinding is only supported for KoH runtimes.");
        if (!Enum.IsDefined(runtime.Provider)) errors.Add("Runtime provider is invalid.");
        if (!Enum.IsDefined(runtime.Allocation)) errors.Add("Runtime allocation is invalid.");
        if (!Enum.IsDefined(runtime.FlagSource)) errors.Add("Runtime flag source is invalid.");
        if (string.IsNullOrWhiteSpace(runtime.RunnerPool) || runtime.RunnerPool.Length > 256)
            errors.Add("Runtime RunnerPool must contain 1..256 characters.");
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
        switch (runtime.Definition)
        {
            case ContainerRuntimeDefinition container:
                if (runtime.Provider is not (RuntimeProvider.Docker or RuntimeProvider.Kubernetes))
                    errors.Add("Container runtimes require the Docker or Kubernetes provider.");
                if (string.IsNullOrWhiteSpace(container.Image))
                    errors.Add("Runtime image is required.");
                else if (container.Image.Length > 512)
                    errors.Add("Runtime image cannot exceed 512 characters.");
                foreach (var port in container.PortMappings ?? new Dictionary<int, int>())
                {
                    if (port.Key is < 1 or > 65535)
                        errors.Add($"Runtime container port {port.Key} is invalid.");
                    if (port.Value is < 0 or > 65535)
                        errors.Add($"Runtime host port {port.Value} is invalid.");
                }
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
                if (runtime.Provider is not (RuntimeProvider.Docker or RuntimeProvider.Kubernetes))
                    errors.Add("Compose runtimes require the Docker or Kubernetes provider.");
                if (string.IsNullOrWhiteSpace(compose.ComposeYaml))
                    errors.Add("Compose runtimes require ComposeYaml.");
                break;
            case OvaRuntimeDefinition ova:
                if (runtime.Provider != RuntimeProvider.Libvirt)
                    errors.Add("OvaVm runtimes require the Libvirt provider.");
                if (string.IsNullOrWhiteSpace(ova.OvaSourceUrl)
                    || !Uri.TryCreate(ova.OvaSourceUrl, UriKind.Absolute, out var source)
                    || source.Scheme is not ("https" or "file"))
                    errors.Add("OvaVm runtimes require an absolute https or file OVA source URL.");
                if (runtime.FlagSource != RuntimeFlagSource.Static)
                    errors.Add("OvaVm runtimes only support static flags.");
                break;
        }
        if (runtime.TtlSeconds is <= 0 or > 604800)
            errors.Add("Runtime TtlSeconds must be between 1 and 604800 when configured.");
        if (runtime.OperationTimeoutSeconds is <= 0 or > 300)
            errors.Add("Runtime OperationTimeoutSeconds must be between 1 and 300 when configured.");
        if (runtime.Limits is not { } limits)
            errors.Add("Runtime resource limits are required.");
        else if (limits.MemoryBytes <= 0 || limits.NanoCpus <= 0 || limits.PidsLimit <= 0)
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
