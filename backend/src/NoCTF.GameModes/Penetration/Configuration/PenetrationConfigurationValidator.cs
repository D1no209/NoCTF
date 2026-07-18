namespace NoCTF.GameModes.Penetration.Configuration;

public static class PenetrationConfigurationValidator
{
    public static IReadOnlyList<string> Validate(PenetrationChallengeConfiguration configuration)
    {
        var errors = new List<string>();
        var ids = configuration.Stages.Select(stage => stage.Id).ToHashSet();
        var duplicateNumbers = configuration.Stages
            .GroupBy(stage => stage.Number)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        foreach (var number in duplicateNumbers)
            errors.Add($"Stage number {number} is duplicated.");

        foreach (var stage in configuration.Stages)
        {
            foreach (var prerequisite in stage.PrerequisiteIds.Where(id => !ids.Contains(id)))
                errors.Add($"Stage {stage.Number} references missing prerequisite {prerequisite}.");
        }

        if (HasCycle(configuration.Stages))
            errors.Add("Stage prerequisites contain a cycle.");
        return errors;
    }

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
