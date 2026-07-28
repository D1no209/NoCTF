namespace NoCTF.GameModes.Awd.Configuration;

using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Flags;

public static class AwdConfigurationValidator
{
    public static IReadOnlyList<string> Validate(AwdConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.HardeningDurationSeconds < 0) errors.Add("HardeningDurationSeconds cannot be negative.");
        if (configuration.RoundDurationSeconds <= 0) errors.Add("RoundDurationSeconds must be positive.");
        if (configuration.CheckerIntervalSeconds <= 0) errors.Add("CheckerIntervalSeconds must be positive.");
        if (configuration.AttackPoints < 0 || configuration.VictimDefensePoolPoints < 0
            || configuration.ServiceHealthyPoints < 0 || configuration.ServiceUnhealthyPenalty < 0)
            errors.Add("Scoring values cannot be negative.");
        if (configuration.FlagTemplate is { } flagTemplate
            && !PerTeamFlagGenerator.IsValidTemplate(flagTemplate))
            errors.Add("FlagTemplate is invalid.");
        return errors;
    }

    public static IReadOnlyList<string> Validate(AwdChallengeConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.AttackPoints < 0 || configuration.VictimDefensePoolPoints < 0
            || configuration.ServiceHealthyPoints < 0 || configuration.ServiceUnhealthyPenalty < 0)
            errors.Add("Scoring overrides cannot be negative.");
        if (configuration.CheckerIntervalSeconds is <= 0)
            errors.Add("CheckerIntervalSeconds must be positive when configured.");
        if (configuration.FlagTemplate is { } flagTemplate
            && !PerTeamFlagGenerator.IsValidTemplate(flagTemplate))
            errors.Add("FlagTemplate is invalid.");
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(
            configuration.Runtime,
            internalEndpointBinding: CheckerInternalBinding(configuration.Checker)));
        if (configuration.Runtime is { Allocation: not RuntimeAllocation.PerTeam })
            errors.Add("AWD runtimes must use PerTeam allocation.");
        if (configuration.Runtime?.Definition is not null
            and not (ContainerRuntimeDefinition or ComposeRuntimeDefinition))
            errors.Add("AWD runtimes only support Container or Compose.");
        errors.AddRange(Registration.RunnerJobConfigurationValidator.Validate(
            configuration.Checker?.Job,
            "Checker"));
        if (configuration.Checker?.Job is
            {
                Provider: not (RuntimeProvider.Docker or RuntimeProvider.Kubernetes)
            })
        {
            errors.Add("AWD Checker requires the Docker or Kubernetes provider.");
        }
        if (configuration.Checker is { } checker)
            ValidateChecker(configuration.Runtime, checker, errors);
        if (configuration.Runtime is not null && configuration.FlagInjection is null)
            errors.Add("FlagInjection is required when Runtime is configured.");
        if (configuration.FlagInjection is { } injection)
        {
            if (string.IsNullOrWhiteSpace(injection.Command)
                || !injection.Command.Contains("${FLAG}", StringComparison.Ordinal))
                errors.Add("FlagInjection.Command must be a non-empty raw template containing ${FLAG}.");
            if (injection.TimeoutSeconds <= 0)
                errors.Add("FlagInjection.TimeoutSeconds must be positive.");
            if (configuration.Runtime?.Definition is ComposeRuntimeDefinition
                && string.IsNullOrWhiteSpace(injection.ServiceName))
                errors.Add("FlagInjection.ServiceName is required for Compose runtimes.");
        }
        return errors;
    }

    private static void ValidateChecker(
        ChallengeRuntimeTemplate? runtime,
        AwdCheckerConfiguration checker,
        List<string> errors)
    {
        if (checker.Job is null)
            return;
        if (checker.Target is null)
        {
            errors.Add("Checker.Target is required.");
            return;
        }
        if (runtime is null)
        {
            errors.Add("Runtime is required when Checker is configured.");
            return;
        }
        if (runtime.Provider != checker.Job.Provider)
            errors.Add("AWD Runtime and Checker must use the same provider.");
        if (checker.Target.ContainerPort is < 1 or > 65535)
            errors.Add("Checker.Target.ContainerPort must be a valid TCP port.");
        ValidateTargetTemplate(checker.Target.UrlTemplate, errors);

        switch (runtime.Definition, checker.Target)
        {
            case (ContainerRuntimeDefinition, ContainerAwdCheckerTarget):
                break;
            case (ComposeRuntimeDefinition, ComposeAwdCheckerTarget):
                break;
            default:
                errors.Add("Checker.Target kind must match the AWD Runtime kind.");
                break;
        }
    }

    private static RuntimeInternalEndpointBinding? CheckerInternalBinding(
        AwdCheckerConfiguration? checker) => checker?.Target switch
    {
        ComposeAwdCheckerTarget target => new(
            target.UrlTemplate,
            target.ContainerPort,
            target.ServiceName),
        _ => null
    };

    private static void ValidateTargetTemplate(string template, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            errors.Add("Checker.Target.UrlTemplate is required.");
            return;
        }
        var remaining = template
            .Replace("{HOST}", string.Empty, StringComparison.Ordinal)
            .Replace("{PORT}", string.Empty, StringComparison.Ordinal);
        if (remaining.Contains('{', StringComparison.Ordinal)
            || remaining.Contains('}', StringComparison.Ordinal))
            errors.Add("Checker.Target.UrlTemplate contains an unsupported placeholder.");
        var probe = template
            .Replace("{HOST}", "runtime.internal", StringComparison.Ordinal)
            .Replace("{PORT}", "8080", StringComparison.Ordinal);
        if (!Uri.TryCreate(probe, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            errors.Add("Checker.Target.UrlTemplate must expand to an absolute HTTP(S) URI.");
    }
}
