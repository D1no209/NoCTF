namespace NoCTF.GameModes.Awdp.Configuration;

using NoCTF.Application.Scoring;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Scoring;

public static class AwdpConfigurationValidator
{
    private static readonly ScoreCurveEvaluator ScoreCurve = new();

    public static IReadOnlyList<string> Validate(AwdpConfiguration configuration, int eligibleTeamCount = 1)
    {
        var errors = new List<string>();
        if (configuration.RoundDurationSeconds <= 0) errors.Add("RoundDurationSeconds must be positive.");
        if (configuration.Break is null)
            errors.Add("Break achievement configuration is required.");
        if (configuration.Fix is null)
            errors.Add("Fix achievement configuration is required.");
        if (configuration.Break is not null)
            errors.AddRange(ScoreCurve.Validate(configuration.Break, eligibleTeamCount));
        if (configuration.Fix is not null)
            errors.AddRange(ScoreCurve.Validate(configuration.Fix, eligibleTeamCount));
        if (configuration.BreakWrongPenalty < 0
            || configuration.FixFailurePenalty < 0
            || configuration.ViolationPenalty < 0
            || configuration.ServiceDownPenalty < 0)
            errors.Add("Penalty values cannot be negative.");
        if (configuration.BreakWrongPenalty > ScoreValueLimits.MaximumConfiguredValue
            || configuration.FixFailurePenalty > ScoreValueLimits.MaximumConfiguredValue
            || configuration.ViolationPenalty > ScoreValueLimits.MaximumConfiguredValue
            || configuration.ServiceDownPenalty > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"Penalty values cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
        if (!Enum.IsDefined(configuration.EvaluationDispatchMode))
            errors.Add("EvaluationDispatchMode is invalid.");
        if (configuration.FlagTemplate is { } flagTemplate
            && !PerTeamFlagGenerator.IsValidTemplate(flagTemplate))
            errors.Add("FlagTemplate is invalid.");
        return errors;
    }

    public static IReadOnlyList<string> Validate(
        AwdpChallengeConfiguration configuration,
        int eligibleTeamCount = 1)
    {
        var errors = new List<string>();
        if (configuration.Break is not null)
            errors.AddRange(ScoreCurve.Validate(configuration.Break, eligibleTeamCount));
        if (configuration.Fix is not null)
            errors.AddRange(ScoreCurve.Validate(configuration.Fix, eligibleTeamCount));
        if (configuration.BreakWrongPenalty is < 0
            || configuration.FixFailurePenalty is < 0
            || configuration.ViolationPenalty is < 0
            || configuration.ServiceDownPenalty is < 0)
            errors.Add("Penalty values cannot be negative.");
        if (configuration.BreakWrongPenalty > ScoreValueLimits.MaximumConfiguredValue
            || configuration.FixFailurePenalty > ScoreValueLimits.MaximumConfiguredValue
            || configuration.ViolationPenalty > ScoreValueLimits.MaximumConfiguredValue
            || configuration.ServiceDownPenalty > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"Penalty values cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
        if (configuration.EvaluationDispatchMode is { } dispatchMode
            && !Enum.IsDefined(dispatchMode))
            errors.Add("EvaluationDispatchMode is invalid.");
        if (configuration.FlagTemplate is { } flagTemplate
            && !PerTeamFlagGenerator.IsValidTemplate(flagTemplate))
            errors.Add("FlagTemplate is invalid.");
        if (configuration.PatchEntrypoint is { } patchEntrypoint)
            ValidatePatchEntrypoint(patchEntrypoint, errors);
        if (configuration.PatchTimeoutSeconds is <= 0)
            errors.Add("PatchTimeoutSeconds must be positive when configured.");
        if (configuration.ReadyTimeoutSeconds is <= 0)
            errors.Add("ReadyTimeoutSeconds must be positive when configured.");
        if (configuration.MaximumPatchUploadBytes is <= 0
            or > NoCTF.Application.GameplayFacts.PatchUploads.PatchUploadRules.HardMaximumArchiveBytes)
        {
            errors.Add(
                $"MaximumPatchUploadBytes must be between 1 and {NoCTF.Application.GameplayFacts.PatchUploads.PatchUploadRules.HardMaximumArchiveBytes} bytes when configured.");
        }
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(configuration.Runtime));
        errors.AddRange(Registration.RunnerJobConfigurationValidator.Validate(configuration.Checker, "Checker"));
        ValidateRuntime(configuration.Runtime, true, errors);
        ValidateChecker(configuration.Checker, errors);
        return errors;
    }

    public static IReadOnlyList<string> Validate(
        AwdpEffectiveConfiguration configuration,
        int eligibleTeamCount = 1)
    {
        var errors = new List<string>();
        if (configuration.RoundDurationSeconds <= 0)
            errors.Add("RoundDurationSeconds must be positive.");
        errors.AddRange(ScoreCurve.Validate(configuration.Break, eligibleTeamCount));
        errors.AddRange(ScoreCurve.Validate(configuration.Fix, eligibleTeamCount));
        if (configuration.BreakWrongPenalty < 0
            || configuration.FixFailurePenalty < 0
            || configuration.ViolationPenalty < 0
            || configuration.ServiceDownPenalty < 0)
            errors.Add("Penalty values cannot be negative.");
        if (configuration.BreakWrongPenalty > ScoreValueLimits.MaximumConfiguredValue
            || configuration.FixFailurePenalty > ScoreValueLimits.MaximumConfiguredValue
            || configuration.ViolationPenalty > ScoreValueLimits.MaximumConfiguredValue
            || configuration.ServiceDownPenalty > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"Penalty values cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
        if (!Enum.IsDefined(configuration.EvaluationDispatchMode))
            errors.Add("EvaluationDispatchMode is invalid.");
        if (!PerTeamFlagGenerator.IsValidTemplate(configuration.FlagTemplate))
            errors.Add("FlagTemplate is invalid.");
        ValidatePatchEntrypoint(configuration.PatchEntrypoint, errors);
        if (configuration.PatchTimeoutSeconds <= 0)
            errors.Add("PatchTimeoutSeconds must be positive.");
        if (configuration.ReadyTimeoutSeconds <= 0)
            errors.Add("ReadyTimeoutSeconds must be positive.");
        if (configuration.MaximumPatchUploadBytes is <= 0
            or > NoCTF.Application.GameplayFacts.PatchUploads.PatchUploadRules.HardMaximumArchiveBytes)
        {
            errors.Add(
                $"MaximumPatchUploadBytes must be between 1 and {NoCTF.Application.GameplayFacts.PatchUploads.PatchUploadRules.HardMaximumArchiveBytes} bytes.");
        }
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(
            configuration.Runtime));
        errors.AddRange(Registration.RunnerJobConfigurationValidator.Validate(
            configuration.Checker,
            "Checker"));
        ValidateRuntime(configuration.Runtime, true, errors);
        ValidateChecker(configuration.Checker, errors);
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
        ValidateRuntime(configuration.Runtime, true, errors);
        ValidateChecker(configuration.Checker, errors);
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

    private static void ValidateRuntime(
        NoCTF.Application.Runtime.Provisioning.ChallengeRuntimeTemplate? runtime,
        bool allowsAttackExposure,
        List<string> errors)
    {
        if (allowsAttackExposure
            && runtime is { Allocation: not NoCTF.Application.Runtime.Provisioning.RuntimeAllocation.PerTeam })
            errors.Add("AWDP player Runtime allocation must be PerTeam.");
        if (allowsAttackExposure
            && runtime is { FlagSource: not NoCTF.Application.Runtime.Provisioning.RuntimeFlagSource.PerTeam })
            errors.Add("AWDP player Runtime FlagSource must be PerTeam.");
        if (runtime is not null
            && runtime.Definition is not NoCTF.Application.Runtime.Provisioning.ContainerRuntimeDefinition)
            errors.Add("AWDP requires a Docker or Kubernetes Container runtime.");
        if (!allowsAttackExposure
            && (runtime?.Definition is NoCTF.Application.Runtime.Provisioning.ContainerRuntimeDefinition
            {
                PortMappings: { Count: > 0 }
            }
            || runtime?.UrlBindings is { Count: > 0 }))
        {
            errors.Add(
                "AWDP disposable targets cannot configure public ports or URLs.");
        }
        if (runtime?.Definition is NoCTF.Application.Runtime.Provisioning.ContainerRuntimeDefinition
            { InternalPorts: not { Count: 1 } })
        {
            errors.Add("AWDP target Runtime must declare exactly one InternalPort.");
        }
        if (allowsAttackExposure
            && runtime?.Definition is NoCTF.Application.Runtime.Provisioning.ContainerRuntimeDefinition
            {
                PortMappings: not { Count: 1 }
            })
            errors.Add("AWDP player Runtime must publish exactly one attack port.");
        if (allowsAttackExposure
            && runtime?.Definition is NoCTF.Application.Runtime.Provisioning.ContainerRuntimeDefinition
            {
                PortMappings: { Count: 1 } portMappings,
                InternalPorts: { Count: 1 } internalPorts
            }
            && portMappings.Keys.Single() != internalPorts[0])
            errors.Add("AWDP player Runtime must publish its single checker target port.");
        if (allowsAttackExposure
            && runtime is not null
            && runtime.UrlBindings is not { Count: > 0 })
            errors.Add("AWDP player Runtime must publish an OwnerOnly access URL.");
        if (allowsAttackExposure
            && runtime?.UrlBindings?.Any(binding =>
                binding.Exposure != NoCTF.Application.Runtime.Provisioning.RuntimeExposure.OwnerOnly) == true)
            errors.Add("AWDP player Runtime URL bindings must use OwnerOnly exposure.");
        if (allowsAttackExposure
            && runtime?.Definition is NoCTF.Application.Runtime.Provisioning.ContainerRuntimeDefinition
            {
                InternalPorts: { Count: 1 } internalPortsForUrl
            }
            && runtime.UrlBindings?.Any(binding =>
                binding.ContainerPort != internalPortsForUrl[0]) == true)
            errors.Add("AWDP player Runtime URL bindings must target its checker port.");
    }

    private static void ValidateFlagInjection(
        AwdpFlagInjectionConfiguration? injection,
        List<string> errors)
    {
        if (injection is null)
            return;
        if (!Enum.IsDefined(injection.Kind))
        {
            errors.Add("FlagInjection.Kind is invalid.");
            return;
        }
        switch (injection.Kind)
        {
            case AwdpFlagInjectionKind.EnvironmentVariable:
                if (string.IsNullOrWhiteSpace(injection.EnvironmentVariableName)
                    || !System.Text.RegularExpressions.Regex.IsMatch(
                        injection.EnvironmentVariableName,
                        "^[A-Za-z_][A-Za-z0-9_]*$",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                    errors.Add("FlagInjection.EnvironmentVariableName is invalid.");
                if (injection.FilePath is not null)
                    errors.Add("FlagInjection.FilePath is only valid for file injection.");
                break;
            case AwdpFlagInjectionKind.File:
                if (string.IsNullOrWhiteSpace(injection.FilePath)
                    || !Path.IsPathRooted(injection.FilePath))
                    errors.Add("FlagInjection.FilePath must be an absolute path.");
                if (injection.EnvironmentVariableName is not null)
                    errors.Add("FlagInjection.EnvironmentVariableName is only valid for environment injection.");
                break;
        }
    }

    private static void ValidateChecker(
        NoCTF.Application.Runtime.Configuration.RunnerJobConfiguration? checker,
        List<string> errors)
    {
        if (checker?.Environment?.Keys.Any(name =>
                name.Equals("TARGET_HOST", StringComparison.OrdinalIgnoreCase)
                || name.Equals(
                    "TARGET_READY_TIMEOUT_SECONDS",
                    StringComparison.OrdinalIgnoreCase)) == true)
        {
            errors.Add(
                "AWDP Checker environment cannot configure Runner-reserved variables.");
        }
    }
}
