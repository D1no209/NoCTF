namespace NoCTF.GameModes.Penetration.Configuration;

public static class PenetrationConfigurationValidator
{
    public static IReadOnlyList<string> Validate(PenetrationChallengeConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.MaxFlagAttempts is <= 0)
            errors.Add("MaxFlagAttempts must be positive when configured.");
        var ids = configuration.Stages.Select(stage => stage.Id).ToHashSet();
        if (ids.Count != configuration.Stages.Count)
            errors.Add("Stage IDs must be unique.");
        var duplicateNumbers = configuration.Stages
            .GroupBy(stage => stage.Number)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        foreach (var number in duplicateNumbers)
            errors.Add($"Stage number {number} is duplicated.");

        foreach (var stage in configuration.Stages)
        {
            if (stage.Number <= 0)
                errors.Add($"Stage number {stage.Number} must be positive.");
            if (string.IsNullOrWhiteSpace(stage.Name))
                errors.Add($"Stage {stage.Number} name is required.");
            if (stage.InjectionKey is { } injectionKey
                && (string.IsNullOrWhiteSpace(injectionKey)
                    || !IsEnvironmentKey(injectionKey)))
                errors.Add($"Stage {stage.Number} InjectionKey must be a valid environment variable name.");
            if (stage.Points is not null)
                errors.AddRange(Ctf.Configuration.CtfConfigurationValidator.ValidatePoints(stage.Points));
            foreach (var prerequisite in stage.PrerequisiteIds.Where(id => !ids.Contains(id)))
                errors.Add($"Stage {stage.Number} references missing prerequisite {prerequisite}.");
        }

        if (HasCycle(configuration.Stages))
            errors.Add("Stage prerequisites contain a cycle.");
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(configuration.Runtime));
        return errors;
    }

    private static bool IsEnvironmentKey(string value) =>
        value.Length <= 128
        && (char.IsAsciiLetter(value[0]) || value[0] == '_')
        && value.Skip(1).All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static bool HasCycle(IReadOnlyList<PenetrationStage> stages)
    {
        var prerequisites = stages.ToDictionary(stage => stage.Id, stage => stage.PrerequisiteIds);
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        return stages.Any(stage => Visit(stage.Id));

        bool Visit(Guid id)
        {
            if (visited.Contains(id)) return false;
            if (!visiting.Add(id)) return true;
            foreach (var dependency in prerequisites.GetValueOrDefault(id, []))
            {
                if (prerequisites.ContainsKey(dependency) && Visit(dependency)) return true;
            }
            visiting.Remove(id);
            visited.Add(id);
            return false;
        }
    }
}
