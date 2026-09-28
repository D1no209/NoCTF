using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Scoring;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.GameplayFacts.PatchVerification;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Scoring;
using NoCTF.Domain.Challenges;

namespace NoCTF.GameModes.Ctf.Configuration;

public static class CtfConfigurationValidator
{
    private static readonly ScoreCurveEvaluator ScoreCurve = new();

    public static IReadOnlyList<string> Validate(CtfConfiguration configuration) => Validate(configuration, 1);

    public static IReadOnlyList<string> Validate(CtfConfiguration configuration, int eligibleTeamCount)
    {
        var errors = ScoreCurve.Validate(configuration.DefaultScoreCurve, eligibleTeamCount).ToList();
        if (configuration.WrongSubmissionPenalty is < 0 or > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"WrongSubmissionPenalty must be between zero and {ScoreValueLimits.MaximumConfiguredValue}.");
        if (configuration.BloodRewards.Count > 3)
            errors.Add("At most three blood rewards are supported.");
        foreach (var reward in configuration.BloodRewards)
        {
            if (reward.Value < 0)
                errors.Add("Blood reward values cannot be negative.");
            if (reward.Policy == BloodRewardPolicy.FixedPoints
                && reward.Value > ScoreValueLimits.MaximumConfiguredValue)
                errors.Add($"Fixed blood rewards cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
            if (reward.Policy != BloodRewardPolicy.FixedPoints && reward.Value > 100)
                errors.Add("Blood reward percentages cannot exceed 100.");
        }
        ValidateFlagTemplate(configuration.FlagTemplate, errors);
        return errors;
    }

    public static IReadOnlyList<string> Validate(CtfChallengeConfiguration configuration) =>
        Validate(configuration, null, 1);

    public static IReadOnlyList<string> Validate(
        CtfChallengeConfiguration configuration,
        CtfConfiguration? competitionConfiguration,
        int eligibleTeamCount)
    {
        var errors = configuration.ScoreCurve is null
            ? []
            : ScoreCurve.Validate(configuration.ScoreCurve, eligibleTeamCount).ToList();
        if (configuration.WrongSubmissionPenalty is < 0 or > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"WrongSubmissionPenalty must be between zero and {ScoreValueLimits.MaximumConfiguredValue}.");
        if (configuration.BloodRewards is { } rewards)
        {
            if (rewards.Count > 3)
                errors.Add("At most three blood rewards are supported.");
            foreach (var reward in rewards)
            {
                if (reward.Value < 0)
                    errors.Add("Blood reward values cannot be negative.");
                if (reward.Policy == BloodRewardPolicy.FixedPoints
                    && reward.Value > ScoreValueLimits.MaximumConfiguredValue)
                    errors.Add($"Fixed blood rewards cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
                if (reward.Policy != BloodRewardPolicy.FixedPoints && reward.Value > 100)
                    errors.Add("Blood reward percentages cannot exceed 100.");
            }
        }
        ValidateMaxAttempts(configuration.MaxFlagAttempts, errors);
        ValidateMaxPatchAttempts(configuration.MaxPatchAttempts, errors);
        ValidateFlagTemplate(configuration.FlagTemplate, errors);
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(configuration.Runtime));
        if (configuration.Runtime is { Allocation: not RuntimeAllocation.PerTeam })
            errors.Add("CTF runtimes must use PerTeam allocation.");
        if (!Enum.IsDefined(configuration.InteractionKind))
            errors.Add("InteractionKind is invalid.");
        if (configuration.InteractionKind == CtfInteractionKind.FlagSubmission
            && configuration.Runtime is
            {
                FlagSource: not RuntimeFlagSource.Static and not RuntimeFlagSource.PerTeam
            })
        {
            errors.Add("CTF runtimes support only Static or PerTeam flags.");
        }
        if (configuration.InteractionKind == CtfInteractionKind.PatchVerification
            && configuration.Runtime is { FlagSource: not RuntimeFlagSource.Static })
        {
            errors.Add("CTF PatchVerification runtimes cannot inject flags.");
        }
        if ((configuration.Runtime?.UrlBindings ?? [])
            .Any(binding => binding is not null
                && binding.Exposure != RuntimeExposure.OwnerOnly))
        {
            errors.Add("CTF runtime URL bindings must use OwnerOnly exposure.");
        }
        ValidateInteractionDefinition(configuration, errors);
        var effectiveCurve = configuration.ScoreCurve ?? competitionConfiguration?.DefaultScoreCurve;
        if (effectiveCurve is not null && configuration.ScoreCurve is null)
            errors.AddRange(ScoreCurve.Validate(effectiveCurve, eligibleTeamCount));
        return errors;
    }

    private static void ValidateMaxAttempts(int? maxAttempts, ICollection<string> errors)
    {
        if (maxAttempts is <= 0)
            errors.Add("MaxFlagAttempts must be positive when configured.");
    }

    private static void ValidateMaxPatchAttempts(int? maxAttempts, ICollection<string> errors)
    {
        if (maxAttempts is <= 0)
            errors.Add("MaxPatchAttempts must be positive when configured.");
    }

    private static void ValidateInteractionDefinition(
        CtfChallengeConfiguration configuration,
        List<string> errors)
    {
        var hasPatchDefinition = configuration.PatchEntrypoint is not null
            || configuration.PatchCommand is not null
            || configuration.PatchTimeoutSeconds is not null
            || configuration.Checker is not null
            || configuration.ReadyTimeoutSeconds is not null
            || configuration.MaximumPatchUploadBytes is not null
            || configuration.CheckerFixInput
            || configuration.CheckerAllowRoot;
        if (configuration.InteractionKind == CtfInteractionKind.FlagSubmission)
        {
            if (hasPatchDefinition)
                errors.Add("FlagSubmission definitions cannot contain PatchVerification settings.");
            return;
        }

        if (configuration.FlagTemplate is not null)
            errors.Add("PatchVerification challenges cannot configure FlagTemplate.");
        if (configuration.Runtime is not null
            && configuration.Runtime.Definition is not ContainerRuntimeDefinition)
        {
            errors.Add("CTF PatchVerification requires a Docker or Kubernetes Container runtime.");
        }
        if (configuration.Runtime?.Definition is ContainerRuntimeDefinition
            { InternalPorts: not { Count: 1 } })
        {
            errors.Add("CTF PatchVerification Runtime must declare exactly one InternalPort.");
        }
        if (configuration.PatchEntrypoint is { } entrypoint)
        {
            if (string.IsNullOrWhiteSpace(entrypoint)
                || entrypoint.Length > 256
                || Path.IsPathRooted(entrypoint)
                || entrypoint.Split('/', '\\').Any(segment => segment is "." or ".."))
            {
                errors.Add("PatchEntrypoint must be a safe relative path of at most 256 characters.");
            }
        }
        if (configuration.PatchCommand is { Count: > 0 } command)
        {
            if (command.Count > PatchVerificationCommandRules.MaximumArguments
                || command.Any(string.IsNullOrWhiteSpace)
                || command.Any(argument =>
                    argument.Length > PatchVerificationCommandRules.MaximumArgumentLength)
                || command.Count(argument => string.Equals(
                    argument,
                    PatchVerificationCommandRules.EntrypointPlaceholder,
                    StringComparison.Ordinal)) != 1)
            {
                errors.Add("PatchCommand must contain one {entrypoint} argument and use valid bounded arguments.");
            }
        }
        if (configuration.PatchTimeoutSeconds is <= 0
            or > PatchVerificationExecutionBudget.MaximumPatchTimeoutSeconds)
        {
            errors.Add($"PatchTimeoutSeconds must be between 1 and {PatchVerificationExecutionBudget.MaximumPatchTimeoutSeconds} when configured.");
        }
        if (configuration.ReadyTimeoutSeconds is <= 0)
            errors.Add("ReadyTimeoutSeconds must be positive when configured.");
        if (configuration.MaximumPatchUploadBytes is <= 0
            or > PatchUploadRules.HardMaximumArchiveBytes)
        {
            errors.Add($"MaximumPatchUploadBytes must be between 1 and {PatchUploadRules.HardMaximumArchiveBytes} bytes when configured.");
        }
        errors.AddRange(Registration.RunnerJobConfigurationValidator.Validate(
            configuration.Checker,
            "Checker"));
        if (configuration.Checker?.Environment?.Keys.Any(name =>
                name.Equals("TARGET_HOST", StringComparison.OrdinalIgnoreCase)
                || name.Equals("TARGET_READY_TIMEOUT_SECONDS", StringComparison.OrdinalIgnoreCase)) == true)
        {
            errors.Add("CTF PatchVerification Checker cannot configure Runner-reserved variables.");
        }
        if (configuration.CheckerFixInput && configuration.Checker is null)
            errors.Add("CheckerFixInput requires Checker.");
        if (configuration.CheckerAllowRoot && configuration.Checker is null)
            errors.Add("CheckerAllowRoot requires Checker.");
        if (configuration.Checker is { TimeoutSeconds: > 0 } checker)
        {
            var patchTimeout = configuration.PatchTimeoutSeconds
                ?? PatchVerificationExecutionBudget.DefaultPatchTimeoutSeconds;
            var readyTimeout = configuration.ReadyTimeoutSeconds
                ?? PatchVerificationExecutionBudget.DefaultReadyTimeoutSeconds;
            if (readyTimeout > checker.TimeoutSeconds)
                errors.Add("ReadyTimeoutSeconds cannot exceed Checker.TimeoutSeconds.");
            if (!PatchVerificationExecutionBudget.FitsHandlerTimeout(
                patchTimeout,
                checker.TimeoutSeconds))
            {
                errors.Add("PatchVerification execution budget must remain below the handler timeout.");
            }
        }
    }

    private static void ValidateFlagTemplate(
        PerTeamFlagTemplate? template,
        ICollection<string> errors)
    {
        if (template is not null && !PerTeamFlagGenerator.IsValidTemplate(template))
            errors.Add("FlagTemplate is invalid.");
    }

}
