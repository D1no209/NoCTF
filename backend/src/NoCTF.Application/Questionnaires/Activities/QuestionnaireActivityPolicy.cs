using NoCTF.Domain.Questionnaires.Activities;

namespace NoCTF.Application.Questionnaires.Activities;

public enum QuestionnaireActivityFailure
{
    InvalidKind, InvalidName, InvalidSchedule, InvalidDuration, InvalidAttemptLimit, InvalidScoringMode,
    NoRules, InvalidRule, DuplicateRule, InvalidFilter, PointsOverflow
}

public static class QuestionnaireActivityPolicy
{
    public static QuestionnaireActivityFailure? Validate(QuestionnaireActivity activity)
    {
        if (!Enum.IsDefined(activity.Kind)) return QuestionnaireActivityFailure.InvalidKind;
        if (string.IsNullOrWhiteSpace(activity.Name) || activity.Name.Length > 160) return QuestionnaireActivityFailure.InvalidName;
        if (activity.OpensAt >= activity.ClosesAt) return QuestionnaireActivityFailure.InvalidSchedule;
        if (activity.DurationSeconds <= 0) return QuestionnaireActivityFailure.InvalidDuration;
        if (!Enum.IsDefined(activity.ScoringMode)) return QuestionnaireActivityFailure.InvalidScoringMode;
        if (activity.AttemptLimit <= 0 || activity.AttemptLimit is null
            && (activity.Kind != QuestionnaireActivityKind.Practice || activity.ScoringMode == QuestionnaireScoringMode.Cumulative))
            return QuestionnaireActivityFailure.InvalidAttemptLimit;
        if (!Enum.IsDefined(activity.SelectionKind)) return QuestionnaireActivityFailure.InvalidRule;
        if (activity.SelectionKind == QuestionnaireSelectionKind.Fixed)
        {
            if (activity.FixedQuestionnaires.Count is < 1 or > 128 || activity.Rules.Count != 0) return QuestionnaireActivityFailure.NoRules;
            if (activity.FixedQuestionnaires.Any(value => value.ChallengeId == Guid.Empty || value.PointsPerQuestion <= 0))
                return QuestionnaireActivityFailure.InvalidRule;
            if (activity.FixedQuestionnaires.Select(value => value.ChallengeId).Distinct().Count() != activity.FixedQuestionnaires.Count)
                return QuestionnaireActivityFailure.DuplicateRule;
            return null;
        }
        if (activity.FixedQuestionnaires.Count != 0) return QuestionnaireActivityFailure.InvalidRule;
        if (activity.Rules.Count == 0) return QuestionnaireActivityFailure.NoRules;
        if (activity.Rules.Any(rule => rule.Id == Guid.Empty)
            || activity.Rules.Select(rule => rule.Id).Distinct().Count() != activity.Rules.Count
            || !activity.Rules.Select(rule => rule.Order).Order().SequenceEqual(Enumerable.Range(0, activity.Rules.Count)))
            return QuestionnaireActivityFailure.DuplicateRule;
        if (activity.Rules.Any(rule => !Enum.IsDefined(rule.Kind)
            || rule.Difficulty is { } difficulty && !Enum.IsDefined(difficulty)
            || rule.Count <= 0 || rule.Points <= 0)) return QuestionnaireActivityFailure.InvalidRule;
        if (activity.Rules.Any(rule => rule.Filters.Any(filter => !Enum.IsDefined(filter.Kind)
            || string.IsNullOrWhiteSpace(filter.NormalizedName) || filter.NormalizedName.Length > 80
            || filter.NormalizedName != filter.NormalizedName.Trim().ToUpperInvariant())
            || rule.Filters.Select(filter => (filter.Kind, filter.NormalizedName)).Distinct().Count() != rule.Filters.Count))
            return QuestionnaireActivityFailure.InvalidFilter;
        try
        {
            _ = activity.Rules.Aggregate(0, (sum, rule) => checked(sum + rule.Count));
            foreach (var rule in activity.Rules) _ = checked(rule.Count * (activity.AttemptLimit ?? 1));
            var total = activity.Rules.Aggregate(0L, (sum, rule) => checked(sum + (long)rule.Count * rule.Points));
            _ = checked(total * (activity.ScoringMode == QuestionnaireScoringMode.Cumulative ? activity.AttemptLimit!.Value : 1));
        }
        catch (OverflowException) { return QuestionnaireActivityFailure.PointsOverflow; }
        return null;
    }
}
