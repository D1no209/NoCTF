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
    public static IReadOnlyList<string> Validate(ChallengeRuntimeTemplate? runtime)
    {
        if (runtime is null) return [];
        var errors = new List<string>();
        if (!Enum.IsDefined(runtime.Provider)) errors.Add("Runtime provider is invalid.");
        if (!Enum.IsDefined(runtime.Allocation)) errors.Add("Runtime allocation is invalid.");
        if (!Enum.IsDefined(runtime.RuntimeKind)) errors.Add("Runtime kind is invalid.");
        if (!Enum.IsDefined(runtime.FlagSource)) errors.Add("Runtime flag source is invalid.");
        if (string.IsNullOrWhiteSpace(runtime.RunnerPool) || runtime.RunnerPool.Length > 256)
            errors.Add("Runtime RunnerPool must contain 1..256 characters.");
        if (runtime.RuntimeKind == RuntimeKind.OvaVm)
        {
            if (runtime.Provider != RuntimeProvider.Libvirt)
                errors.Add("OvaVm runtimes require the Libvirt provider.");
            if (string.IsNullOrWhiteSpace(runtime.OvaSourceUrl)
                || !Uri.TryCreate(runtime.OvaSourceUrl, UriKind.Absolute, out var source)
                || source.Scheme is not ("https" or "file"))
                errors.Add("OvaVm runtimes require an absolute https or file OVA source URL.");
            if (runtime.FlagSource != RuntimeFlagSource.Static)
                errors.Add("OvaVm runtimes only support static flags.");
        }
        else if (string.IsNullOrWhiteSpace(runtime.Image)) errors.Add("Runtime image is required.");
        else if (runtime.Image.Length > 512) errors.Add("Runtime image cannot exceed 512 characters.");
        if (runtime.RuntimeKind == RuntimeKind.Compose && runtime.Provider == RuntimeProvider.Libvirt)
            errors.Add("Compose runtimes are not supported by Libvirt.");
        if (runtime.RuntimeKind == RuntimeKind.Container && runtime.Provider == RuntimeProvider.Libvirt
            && runtime.FlagSource == RuntimeFlagSource.AwdRotation)
            errors.Add("Libvirt runtimes cannot use AWD flag rotation.");
        if (runtime.TtlSeconds is <= 0 or > 604800)
            errors.Add("Runtime TtlSeconds must be between 1 and 604800 when configured.");
        if (runtime.OperationTimeoutSeconds is <= 0 or > 300)
            errors.Add("Runtime OperationTimeoutSeconds must be between 1 and 300 when configured.");
        foreach (var port in runtime.PortMappings ?? new Dictionary<int, int>())
        {
            if (port.Key is < 1 or > 65535) errors.Add($"Runtime container port {port.Key} is invalid.");
            if (port.Value is < 0 or > 65535) errors.Add($"Runtime host port {port.Value} is invalid.");
        }
        if (runtime.Limits is { } limits
            && (limits.MemoryBytes <= 0 || limits.NanoCpus <= 0 || limits.PidsLimit <= 0))
            errors.Add("Runtime resource limits must be positive.");
        if (runtime.Security is { } security)
        {
            if (!security.RunAsNonRoot) errors.Add("Runtime security must require a non-root user.");
            if (!security.NoNewPrivileges) errors.Add("Runtime security must disable privilege escalation.");
        }
        foreach (var binding in runtime.UrlBindings ?? [])
        {
            if (!Enum.IsDefined(binding.Exposure) || string.IsNullOrWhiteSpace(binding.UrlTemplate))
                errors.Add("Runtime URL bindings require a valid exposure and template.");
            if (binding.UrlTemplate.Contains('{')
                && !binding.UrlTemplate.Contains("{HOST}", StringComparison.Ordinal)
                && !binding.UrlTemplate.Contains("{PORT}", StringComparison.Ordinal))
                errors.Add("Runtime URL bindings only allow HOST and PORT placeholders.");
            if (binding.ContainerPort is < 1 or > 65535 || binding.GuestPort is < 1 or > 65535)
                errors.Add("Runtime URL binding ports must be between 1 and 65535.");
        }
        return errors;
    }
}
