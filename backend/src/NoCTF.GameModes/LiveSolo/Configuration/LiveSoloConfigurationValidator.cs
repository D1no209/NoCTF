using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Challenges;

namespace NoCTF.GameModes.LiveSolo.Configuration;

public static class LiveSoloConfigurationValidator
{
    public static IReadOnlyList<string> Validate(LiveSoloCompetitionModeConfiguration value)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(value.BracketFormat)) errors.Add("Invalid LiveSolo bracket format.");
        if (value.RequiredWins is < 1 or > 1024) errors.Add("RequiredWins must be between 1 and 1024.");
        if (value.CountdownSeconds is < 1 or > 60) errors.Add("CountdownSeconds must be between 1 and 60.");
        if (value.QuestionIntervalSeconds is < 1 or > 86400) errors.Add("QuestionIntervalSeconds must be between 1 and 86400.");
        if (value.RoundLimitSeconds is < 1 or > 86400) errors.Add("RoundLimitSeconds must be between 1 and 86400.");
        if (value.PublicDelaySeconds is < 0 or > 86400) errors.Add("PublicDelaySeconds must be between 0 and 86400.");
        if (value.RecordingRetentionDays is < 1 or > 3650) errors.Add("RecordingRetentionDays must be between 1 and 3650.");
        if (value.MaximumConcurrentMatches is < 1 or > 128) errors.Add("MaximumConcurrentMatches must be between 1 and 128.");
        if (value.MaximumRosterMembers is < 1 or > 64) errors.Add("MaximumRosterMembers must be between 1 and 64.");
        if (value.MaximumViewers is < 1 or > 100000) errors.Add("MaximumViewers must be between 1 and 100000.");
        if (value.StageRules.Any(x => !Enum.IsDefined(x.Lane) || x.Stage < 1 || x.RequiredWins is < 1 or > 1024))
            errors.Add("Stage rules require a valid lane, positive stage and RequiredWins between 1 and 1024.");
        if (value.StageRules.GroupBy(x => (x.Lane, x.Stage)).Any(x => x.Count() > 1)) errors.Add("Duplicate LiveSolo stage rules.");
        return errors;
    }

    public static IReadOnlyList<string> ValidateDefinition(LiveSoloChallengeDefinition value)
    {
        if (value.Runtime is { Allocation: not PersistedRuntimeAllocation.PerTeam })
            return ["LiveSolo Runtime resources must be independent per team."];
        if (value.Checker is not null || value.PatchEntrypoint is not null || value.PatchTimeoutSeconds is not null
            || value.MaximumPatchUploadBytes is not null || value.CheckerFixInput
            || value.StringItems.Any(x => x.Kind is ChallengeDefinitionStringKind.PatchCommand or ChallengeDefinitionStringKind.CheckerCommand))
            return ["LiveSolo accepts FlagSubmission definitions, not PatchVerification or checker-based interactions."];
        return [];
    }

    public static IReadOnlyList<string> ValidateGroup(LiveSoloQuestionGroup value, int defaultLimit, int defaultInterval)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(value.Name) || value.Name.Length > 160) errors.Add("A question group name is required.");
        if (value.Items.Count is < 1 or > 64) errors.Add("A question group requires between 1 and 64 questions.");
        if (value.RoundLimitSeconds is < 1 or > 86400) errors.Add("Invalid group RoundLimitSeconds.");
        if (value.Items.Select(x => x.CompetitionChallengeId).Distinct().Count() != value.Items.Count) errors.Add("A question group cannot repeat a challenge.");
        if (value.Items.Any(x => x.CompetitionChallengeId == Guid.Empty)) errors.Add("A question group must reference challenges.");
        var limit = value.RoundLimitSeconds ?? defaultLimit;
        var prior = -1;
        foreach (var item in value.Items.OrderBy(x => x.Position))
        {
            var offset = item.OpenOffsetSeconds ?? checked(item.Position * defaultInterval);
            if (item.Position == 0 && offset != 0 || offset <= prior || offset >= limit)
                errors.Add("Question offsets must start at zero, increase and remain before the Round limit.");
            prior = offset;
        }
        if (value.Items.Select(x => x.Position).Order().Where((position, index) => position != index).Any())
            errors.Add("Question positions must be contiguous from zero.");
        return errors;
    }
}
