using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;

namespace NoCTF.GameModes.Registration;

public sealed class ChallengeRuntimeTemplateCatalog : IChallengeRuntimeTemplateCatalog
{
    public ChallengeRuntimeTemplate? Get(ChallengeDefinition? definition) =>
        TypedGameModeConfiguration.Runtime(definition?.Runtime);
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
        switch (runtime.Definition)
        {
            case ContainerRuntimeDefinition container:
                errors.AddRange(ContainerRuntimeDefinitionPolicy.Validate(container, runtime.UrlBindings, runtime.ControlCheckUrlBinding, internalEndpointBinding));
                if (runtime.FlagSource == RuntimeFlagSource.PerTeam && !container.Services.Any(service => service.FlagEnvironmentVariableName is not null))
                    errors.Add("PerTeam runtimes require at least one Flag environment target.");
                if (runtime.FlagSource != RuntimeFlagSource.PerTeam && container.Services.Any(service => service.FlagEnvironmentVariableName is not null))
                    errors.Add("Flag environment targets are only supported for PerTeam runtimes.");
                break;
        }
        if (runtime.TtlSeconds is <= 0 or > 604800)
            errors.Add("Runtime TtlSeconds must be between 1 and 604800 when configured.");
        if (runtime.OperationTimeoutSeconds is <= 0 or > 300)
            errors.Add("Runtime OperationTimeoutSeconds must be between 1 and 300 when configured.");
        if (runtime.Definition is OvaRuntimeDefinition && runtime.Limits is not { } limits)
            errors.Add("Runtime resource limits are required.");
        else if (runtime.Definition is OvaRuntimeDefinition && runtime.Limits is { } ovaLimits && (ovaLimits.MemoryBytes <= 0
            || ovaLimits.CpuMillicores <= 0
            || ovaLimits.PidsLimit <= 0))
            errors.Add("Runtime resource limits must be positive.");
        if (runtime.UrlBindings?.Any(binding => binding is null) == true)
            errors.Add("Runtime URL bindings cannot contain null entries.");
        var urlBindings = new List<(RuntimeUrlBinding Binding, bool IsAccessUrl)>();
        urlBindings.AddRange((runtime.UrlBindings ?? [])
            .Where(binding => binding is not null)
            .Select(binding => (binding!, true)));
        if (runtime.ControlCheckUrlBinding is { } controlCheckUrlBinding)
            urlBindings.Add((controlCheckUrlBinding, false));
        foreach (var (binding, isAccessUrl) in urlBindings)
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
                if (!isAccessUrl)
                {
                    var expandedTemplate = binding.UrlTemplate
                        .Replace("{HOST}", "runtime.invalid", StringComparison.Ordinal)
                        .Replace("{PORT}", "1", StringComparison.Ordinal);
                    if (!Uri.TryCreate(expandedTemplate, UriKind.Absolute, out _))
                        errors.Add("Runtime URL bindings must expand to an absolute URI.");
                }
            }
            if (binding.ContainerPort is < 1 or > 65535
                || binding.GuestPort is < 1 or > 65535)
                errors.Add("Runtime URL binding ports must be between 1 and 65535.");
            switch (runtime.Definition?.RuntimeKind)
            {
                case RuntimeKind.Container:
                    break; // Named-service references and ports are validated by the portable policy.
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
        return errors;
    }

}
