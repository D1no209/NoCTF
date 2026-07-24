namespace NoCTF.GameModes.Awdp.Configuration;

public static class AwdpConfigurationValidator
{
    public static IReadOnlyList<string> Validate(AwdpConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.RoundDurationSeconds <= 0) errors.Add("RoundDurationSeconds must be positive.");
        if (configuration.Break is null)
            errors.Add("Break achievement configuration is required.");
        if (configuration.Fix is null)
            errors.Add("Fix achievement configuration is required.");
        if (configuration.Break?.Points < 0 || configuration.Fix?.Points < 0)
            errors.Add("Achievement points cannot be negative.");
        if (configuration.BreakWrongPenalty < 0
            || configuration.FixFailurePenalty < 0
            || configuration.ViolationPenalty < 0
            || configuration.ServiceDownPenalty < 0)
            errors.Add("Penalty values cannot be negative.");
        if (!Enum.IsDefined(configuration.EvaluationDispatchMode))
            errors.Add("EvaluationDispatchMode is invalid.");
        ValidatePatchEntrypoint(configuration.PatchEntrypoint, errors);
        if (configuration.PatchTimeoutSeconds <= 0)
            errors.Add("PatchTimeoutSeconds must be positive.");
        if (configuration.ReadyTimeoutSeconds <= 0)
            errors.Add("ReadyTimeoutSeconds must be positive.");
        if (configuration.TargetPort is not 0 and (< 1 or > 65535))
            errors.Add("TargetPort must be zero or a valid TCP port.");
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(
            configuration.Runtime));
        errors.AddRange(Registration.RunnerJobConfigurationValidator.Validate(
            configuration.Checker,
            "Checker"));
        ValidateRuntime(configuration.Runtime, errors);
        ValidateChecker(configuration.Checker, errors);
        ValidateProviderPair(configuration.Runtime, configuration.Checker, errors);
        return errors;
    }

    public static IReadOnlyList<string> Validate(AwdpChallengeConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.Break?.Points < 0 || configuration.Fix?.Points < 0)
            errors.Add("Achievement points cannot be negative.");
        if (configuration.BreakWrongPenalty is < 0
            || configuration.FixFailurePenalty is < 0
            || configuration.ViolationPenalty is < 0
            || configuration.ServiceDownPenalty is < 0)
            errors.Add("Penalty values cannot be negative.");
        if (configuration.EvaluationDispatchMode is { } dispatchMode
            && !Enum.IsDefined(dispatchMode))
            errors.Add("EvaluationDispatchMode is invalid.");
        if (configuration.PatchEntrypoint is { } patchEntrypoint)
            ValidatePatchEntrypoint(patchEntrypoint, errors);
        if (configuration.PatchTimeoutSeconds is <= 0)
            errors.Add("PatchTimeoutSeconds must be positive when configured.");
        if (configuration.ReadyTimeoutSeconds is <= 0)
            errors.Add("ReadyTimeoutSeconds must be positive when configured.");
        if (configuration.TargetPort is not null and (< 1 or > 65535))
            errors.Add("TargetPort must be a valid TCP port when configured.");
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(configuration.Runtime));
        errors.AddRange(Registration.RunnerJobConfigurationValidator.Validate(configuration.Checker, "Checker"));
        ValidateRuntime(configuration.Runtime, errors);
        ValidateChecker(configuration.Checker, errors);
        ValidateProviderPair(configuration.Runtime, configuration.Checker, errors);
        return errors;
    }

    public static IReadOnlyList<string> Validate(AwdpEffectiveConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.RoundDurationSeconds <= 0)
            errors.Add("RoundDurationSeconds must be positive.");
        if (configuration.Break.Points < 0 || configuration.Fix.Points < 0)
            errors.Add("Achievement points cannot be negative.");
        if (configuration.BreakWrongPenalty < 0
            || configuration.FixFailurePenalty < 0
            || configuration.ViolationPenalty < 0
            || configuration.ServiceDownPenalty < 0)
            errors.Add("Penalty values cannot be negative.");
        if (!Enum.IsDefined(configuration.EvaluationDispatchMode))
            errors.Add("EvaluationDispatchMode is invalid.");
        ValidatePatchEntrypoint(configuration.PatchEntrypoint, errors);
        if (configuration.PatchTimeoutSeconds <= 0)
            errors.Add("PatchTimeoutSeconds must be positive.");
        if (configuration.ReadyTimeoutSeconds <= 0)
            errors.Add("ReadyTimeoutSeconds must be positive.");
        if (configuration.TargetPort is not 0 and (< 1 or > 65535))
            errors.Add("TargetPort must be zero or a valid TCP port.");
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(
            configuration.Runtime));
        errors.AddRange(Registration.RunnerJobConfigurationValidator.Validate(
            configuration.Checker,
            "Checker"));
        ValidateRuntime(configuration.Runtime, errors);
        ValidateChecker(configuration.Checker, errors);
        ValidateProviderPair(configuration.Runtime, configuration.Checker, errors);
        return errors;
    }

    public static IReadOnlyList<string> ValidateForStart(
        AwdpEffectiveConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.Runtime is null)
            errors.Add("Runtime is required before an AWDP competition can start.");
        if (configuration.Checker is null)
            errors.Add("Checker is required before an AWDP competition can start.");
        if (configuration.TargetPort is < 1 or > 65535)
            errors.Add("TargetPort must be a valid TCP port before an AWDP competition can start.");
        ValidateRuntime(configuration.Runtime, errors);
        ValidateChecker(configuration.Checker, errors);
        ValidateProviderPair(configuration.Runtime, configuration.Checker, errors);
        return errors;
    }

    private static void ValidatePatchEntrypoint(
        string patchEntrypoint,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(patchEntrypoint))
            errors.Add("PatchEntrypoint is required.");
        else if (patchEntrypoint.Length > 256)
            errors.Add("PatchEntrypoint cannot exceed 256 characters.");
        else if (Path.IsPathRooted(patchEntrypoint)
                 || patchEntrypoint.Split('/', '\\').Any(segment => segment is ".." or "."))
            errors.Add("PatchEntrypoint must be a safe relative path.");
    }

    private static void ValidateProviderPair(
        NoCTF.Application.Runtime.Ports.ChallengeRuntimeTemplate? runtime,
        NoCTF.Application.Runtime.Ports.RunnerJobConfiguration? checker,
        List<string> errors)
    {
        if (runtime is not null
            && checker is not null
            && runtime.Provider != checker.Provider)
            errors.Add("Runtime and Checker must use the same provider.");
    }

    private static void ValidateRuntime(
        NoCTF.Application.Runtime.Ports.ChallengeRuntimeTemplate? runtime,
        List<string> errors)
    {
        if (runtime is not null
            && (runtime.RuntimeKind != NoCTF.Domain.Runtime.RuntimeKind.Container
                || runtime.Provider is not (
                    NoCTF.Domain.Runtime.RuntimeProvider.Docker
                    or NoCTF.Domain.Runtime.RuntimeProvider.Kubernetes)))
            errors.Add(
                "AWDP disposable targets require a Docker or Kubernetes Container runtime.");
        if (runtime?.PortMappings is { Count: > 0 }
            || runtime?.UrlBindings is { Count: > 0 })
        {
            errors.Add(
                "AWDP disposable targets cannot configure public ports or URLs.");
        }
    }

    private static void ValidateChecker(
        NoCTF.Application.Runtime.Ports.RunnerJobConfiguration? checker,
        List<string> errors)
    {
        if (checker is not null
            && checker.Provider is not (
                NoCTF.Domain.Runtime.RuntimeProvider.Docker
                or NoCTF.Domain.Runtime.RuntimeProvider.Kubernetes))
            errors.Add("AWDP Checker requires the Docker or Kubernetes provider.");
    }
}
