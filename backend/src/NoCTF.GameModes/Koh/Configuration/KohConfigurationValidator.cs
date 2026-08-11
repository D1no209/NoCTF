using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Scoring;

namespace NoCTF.GameModes.Koh.Configuration;

public static class KohConfigurationValidator
{
    public static IReadOnlyList<string> Validate(KohConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.PollIntervalSeconds <= 0) errors.Add("PollIntervalSeconds must be positive.");
        if (configuration.ControlPointsPerInterval < 0) errors.Add("ControlPointsPerInterval cannot be negative.");
        if (configuration.ControlPointsPerInterval > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"ControlPointsPerInterval cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
        return errors;
    }

    public static IReadOnlyList<string> Validate(KohChallengeConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.PollIntervalSeconds is <= 0)
            errors.Add("PollIntervalSeconds must be positive when configured.");
        if (configuration.ControlPointsPerInterval is < 0)
            errors.Add("ControlPointsPerInterval cannot be negative when configured.");
        if (configuration.ControlPointsPerInterval > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"ControlPointsPerInterval cannot exceed {ScoreValueLimits.MaximumConfiguredValue} when configured.");
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(
            configuration.Runtime,
            allowControlCheckUrlBinding: true));
        if (configuration.Runtime is { Allocation: not RuntimeAllocation.Shared })
            errors.Add("KoH Hill runtime allocation must be Shared.");
        return errors;
    }

    public static IReadOnlyList<string> ValidateForStart(KohChallengeConfiguration configuration)
    {
        var errors = Validate(configuration).ToList();
        if (configuration.Runtime is null)
            errors.Add("Runtime is required before a KoH competition can start.");
        else
        {
            if (configuration.Runtime.ControlCheckUrlBinding is null)
                errors.Add("ControlCheckUrlBinding is required before a KoH competition can start.");
            if (!(configuration.Runtime.UrlBindings ?? [])
                .Any(binding => binding is not null
                    && binding.Exposure == RuntimeExposure.Participants))
                errors.Add("KoH runtime requires at least one Participants URL binding.");
        }
        return errors;
    }
}
