using NoCTF.Domain.Competitions;
using NoCTF.Domain.Theory.Activities;

namespace NoCTF.Application.Theory.Activities;

public enum TheoryActivityFailure
{
    InvalidKind, UnsupportedGameMode, InvalidName, InvalidSchedule, InvalidDuration,
    InvalidAttemptLimit, InvalidScoringMode, NoRules, InvalidRule, DuplicateRule,
    InvalidFilter, PointsOverflow
}

public static class TheoryActivityPolicy
{
    public static bool Supports(GameMode mode) => mode is GameMode.Ctf or GameMode.Awd or GameMode.Awdp;

    public static TheoryActivityFailure? Validate(TheoryActivity activity, GameMode? competitionMode = null)
    {
        if (!Enum.IsDefined(activity.Kind)) return TheoryActivityFailure.InvalidKind;
        if (activity.Kind == TheoryActivityKind.Competition
            ? competitionMode is null || !Supports(competitionMode.Value)
            : competitionMode is not null) return TheoryActivityFailure.UnsupportedGameMode;
        if (string.IsNullOrWhiteSpace(activity.Name) || activity.Name.Length > 160) return TheoryActivityFailure.InvalidName;
        if (activity.OpensAt >= activity.ClosesAt) return TheoryActivityFailure.InvalidSchedule;
        if (activity.DurationSeconds <= 0) return TheoryActivityFailure.InvalidDuration;
        if (!Enum.IsDefined(activity.ScoringMode)) return TheoryActivityFailure.InvalidScoringMode;
        if (activity.AttemptLimit <= 0 || activity.AttemptLimit is null
            && (activity.Kind != TheoryActivityKind.Practice || activity.ScoringMode == TheoryScoringMode.Cumulative))
            return TheoryActivityFailure.InvalidAttemptLimit;
        if (activity.Rules.Count == 0) return TheoryActivityFailure.NoRules;
        if (activity.Rules.Any(rule => rule.Id == Guid.Empty)
            || activity.Rules.Select(rule => rule.Id).Distinct().Count() != activity.Rules.Count
            || !activity.Rules.Select(rule => rule.Order).Order().SequenceEqual(Enumerable.Range(0, activity.Rules.Count)))
            return TheoryActivityFailure.DuplicateRule;
        if (activity.Rules.Any(rule => !Enum.IsDefined(rule.Kind)
            || rule.Difficulty is { } difficulty && !Enum.IsDefined(difficulty)
            || rule.Count <= 0 || rule.Points <= 0)) return TheoryActivityFailure.InvalidRule;
        if (activity.Rules.Any(rule => rule.Filters.Any(filter => !Enum.IsDefined(filter.Kind)
            || string.IsNullOrWhiteSpace(filter.NormalizedName) || filter.NormalizedName.Length > 80
            || filter.NormalizedName != filter.NormalizedName.Trim().ToUpperInvariant())
            || rule.Filters.Select(filter => (filter.Kind, filter.NormalizedName)).Distinct().Count() != rule.Filters.Count))
            return TheoryActivityFailure.InvalidFilter;
        try
        {
            _ = activity.Rules.Aggregate(0, (sum, rule) => checked(sum + rule.Count));
            foreach (var rule in activity.Rules) _ = checked(rule.Count * (activity.AttemptLimit ?? 1));
            var total = activity.Rules.Aggregate(0L, (sum, rule) => checked(sum + (long)rule.Count * rule.Points));
            _ = checked(total * (activity.ScoringMode == TheoryScoringMode.Cumulative ? activity.AttemptLimit!.Value : 1));
        }
        catch (OverflowException) { return TheoryActivityFailure.PointsOverflow; }
        return null;
    }
}
