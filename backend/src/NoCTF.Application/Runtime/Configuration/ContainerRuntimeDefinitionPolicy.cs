using System.Text.RegularExpressions;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Application.Runtime.Configuration;

/// <summary>Validates the portable named-service authoring contract.</summary>
public static partial class ContainerRuntimeDefinitionPolicy
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceNamePattern();

    public static IReadOnlyList<string> Validate(
        ContainerRuntimeDefinition definition,
        IReadOnlyList<RuntimeUrlBinding>? bindings = null,
        RuntimeUrlBinding? control = null,
        RuntimeInternalEndpointBinding? checker = null)
    {
        var errors = new List<string>();
        if (definition.Services is not { Count: >= 1 and <= 64 })
            return ["Container runtimes require between 1 and 64 services."];
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in definition.Services)
        {
            if (service is null) { errors.Add("Runtime services cannot be null."); continue; }
            if (service.Name is null || !ServiceNamePattern().IsMatch(service.Name))
                errors.Add("Service names must be lowercase DNS labels of 1 to 63 characters.");
            else if (!names.Add(service.Name)) errors.Add($"Duplicate Runtime service '{service.Name}'.");
            if (string.IsNullOrWhiteSpace(service.Image) || service.Image.Length > 512)
                errors.Add("Every service requires an image of at most 512 characters.");
            if (service.MemoryMiB <= 0 || service.MemoryMiB > long.MaxValue / (1024 * 1024)
                || service.CpuCores <= 0 || service.CpuCores > long.MaxValue / 1000m
                || service.CpuCores * 1000m != decimal.Truncate(service.CpuCores * 1000m))
                errors.Add("Service resources must be positive; CPU uses increments of 0.001 cores.");
            if (service.Command?.Any(string.IsNullOrWhiteSpace) == true || service.Arguments?.Any(argument => argument is null) == true)
                errors.Add("Service commands and arguments cannot contain null values or empty command elements.");
            if (service.InternalPorts?.Any(port => port is < 1 or > 65535) == true
                || service.InternalPorts?.Distinct().Count() != service.InternalPorts?.Count)
                errors.Add("Service internal ports must be unique and between 1 and 65535.");
            foreach (var variable in service.Environment ?? new Dictionary<string, string>())
                if (!IsEnvironmentName(variable.Key) || variable.Key.StartsWith("NOCTF_", StringComparison.OrdinalIgnoreCase))
                    errors.Add("Service environment names must be valid and cannot use the NOCTF_ prefix.");
            if (service.FlagEnvironmentVariableName is { } flag
                && (!IsEnvironmentName(flag) || flag.StartsWith("NOCTF_", StringComparison.OrdinalIgnoreCase)))
                errors.Add("Flag environment names must be valid and cannot use the NOCTF_ prefix.");
        }
        foreach (var binding in (bindings ?? []).Concat(control is null ? [] : [control]))
            if (binding is null || binding.ServiceName is null || !names.Contains(binding.ServiceName)
                || binding.ContainerPort is not (>= 1 and <= 65535) || binding.VmId is not null || binding.GuestPort is not null)
                errors.Add("Container entries must reference an existing service and a valid service port.");
        if (checker is not null && (checker.ServiceName is null || !names.Contains(checker.ServiceName)))
            errors.Add("Checker targets must reference an existing service.");
        if (!Enum.IsDefined(definition.EgressPolicy)) errors.Add("Runtime egress policy is invalid.");
        return errors;
    }

    private static bool IsEnvironmentName(string name) => !string.IsNullOrEmpty(name)
        && (char.IsAsciiLetter(name[0]) || name[0] == '_')
        && name.Skip(1).All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}
