namespace NoCTF.API.Endpoints.Admin;

internal static class CompetitionRequestRules
{
    public static IReadOnlyList<string> Validate(
        string? title,
        DateTime startTime,
        DateTime endTime,
        PointsConfigDto? points,
        int maxTeamMembers,
        int? roundDurationSeconds,
        int? totalRounds,
        int? maxAttackAttempts,
        int? maxDefenseAttempts,
        int? fixTimeoutSeconds,
        params int?[] scoreValues)
    {
        var errors = new List<string>();
        var normalizedTitle = title?.Trim() ?? string.Empty;
        if (normalizedTitle.Length is < 1 or > 200)
            errors.Add("Title must contain between 1 and 200 characters.");
        if (endTime <= startTime)
            errors.Add("EndTime must be later than StartTime.");
        if (maxTeamMembers is < 1 or > 50)
            errors.Add("MaxTeamMembers must be between 1 and 50.");
        if (points is not null &&
            (points.InitialPoints <= 0 || points.MinimumPoints <= 0 ||
             points.MinimumPoints > points.InitialPoints || points.DecayFactor <= 0))
        {
            errors.Add("Default point values must be positive and MinimumPoints cannot exceed InitialPoints.");
        }
        if (roundDurationSeconds is not null && roundDurationSeconds is < 5 or > 86400)
            errors.Add("RoundDurationSeconds must be between 5 and 86400.");
        if (totalRounds is not null && totalRounds is < 1 or > 100000)
            errors.Add("TotalRounds must be between 1 and 100000.");
        if (maxAttackAttempts is not null && maxAttackAttempts is < 1 or > 10000)
            errors.Add("AwdpMaxAttackAttempts must be between 1 and 10000.");
        if (maxDefenseAttempts is not null && maxDefenseAttempts is < 1 or > 10000)
            errors.Add("AwdpMaxDefenseAttempts must be between 1 and 10000.");
        if (fixTimeoutSeconds is not null && fixTimeoutSeconds is < 1 or > 3600)
            errors.Add("AwdpFixTimeoutSeconds must be between 1 and 3600.");
        if (scoreValues.Any(value => value is < 0 or > 1_000_000))
            errors.Add("Score and penalty values must be between 0 and 1000000.");
        return errors;
    }
}
