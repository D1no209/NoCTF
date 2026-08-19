using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.GameModes.Scoring;

namespace NoCTF.GameModes.Leaderboard;

internal static class AwdpDynamicLeaderboardProjection
{
    private static readonly ScoreCurveEvaluator ScoreCurve = new();

    public static GameModeLeaderboardProjection Project(LeaderboardProjectionInput input)
    {
        var competition = ParseCompetition(input.CompetitionConfigurationJson);
        var activeTeams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted)
            .ToDictionary(team => team.Id);
        var scoringTeams = activeTeams.Values
            .Where(team => team.EarnsScore)
            .ToDictionary(team => team.Id);
        var dynamicTeams = activeTeams.Values
            .Where(team => team.AffectsDynamicChallengeScore)
            .Select(team => team.Id)
            .ToHashSet();
        var competitiveTeams = activeTeams.Values
            .Where(team => team.AffectsCompetitiveResults)
            .Select(team => team.Id)
            .ToHashSet();
        var challenges = (input.Challenges ?? [])
            .Where(challenge => !challenge.IsDeleted)
            .ToDictionary(challenge => challenge.Id);
        var projectedAt = input.ProjectedAt ?? DateTimeOffset.UtcNow;
        var elapsed = EffectiveElapsed(
            projectedAt,
            input.LifecycleAudits,
            input.CompetitionStartTime);
        var settledThroughRound = SettledThroughRound(
            elapsed,
            competition.RoundDurationSeconds,
            input.CompetitionStatus == CompetitionStatus.Finished);
        var currentRound = input.CompetitionStatus == CompetitionStatus.Finished
            ? Math.Max(1, settledThroughRound)
            : Round(elapsed, competition.RoundDurationSeconds);
        var elapsedSeconds = Math.Max(0L, (long)Math.Floor(elapsed.TotalSeconds));
        var currentRoundRemainingSeconds = input.CompetitionStatus == CompetitionStatus.Finished
            ? 0
            : competition.RoundDurationSeconds
                - (int)(elapsedSeconds % competition.RoundDurationSeconds);
        var correctFacts = input.GameplayFacts
            .Where(fact => fact.OccurredAt <= projectedAt
                && fact.TeamId is Guid teamId
                && activeTeams.ContainsKey(teamId)
                && fact.Kind is GameplayFactKind.BreakAttempt or GameplayFactKind.FixAttempt
                && fact.Result == GameplayFactResult.Correct
                && fact.CompetitionChallengeId is Guid challengeId
                && (challenges.Count == 0 || challenges.ContainsKey(challengeId))
                && (fact.Kind != GameplayFactKind.BreakAttempt
                    || competitiveTeams.Contains(teamId)
                    && (fact.VictimTeamId is null
                        || competitiveTeams.Contains(fact.VictimTeamId.Value))))
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.GameplayFactId)
            .ToList();
        var activations = correctFacts
            .Where(fact =>
            {
                if (fact.Kind != GameplayFactKind.FixAttempt)
                    return true;
                var settings = Effective(
                    competition,
                    challenges.GetValueOrDefault(fact.CompetitionChallengeId!.Value)?.ConfigurationJson);
                return !settings.RequireBreakBeforeFix
                    || correctFacts.Any(candidate =>
                        candidate.Kind == GameplayFactKind.BreakAttempt
                        && candidate.TeamId == fact.TeamId
                        && candidate.CompetitionChallengeId == fact.CompetitionChallengeId
                        && (candidate.OccurredAt < fact.OccurredAt
                            || candidate.OccurredAt == fact.OccurredAt
                            && candidate.GameplayFactId.CompareTo(fact.GameplayFactId) < 0));
            })
            .GroupBy(fact => new
            {
                TeamId = fact.TeamId!.Value,
                ChallengeId = fact.CompetitionChallengeId!.Value,
                fact.Kind
            })
            .Select(group => group.First())
            .Select(fact => new Activation(
                fact,
                Round(
                    fact.OccurredAt,
                    input.LifecycleAudits,
                    input.CompetitionStartTime,
                    competition.RoundDurationSeconds)))
            .ToList();

        var awards = new List<Award>();
        var breakScores = new Dictionary<Guid, long>();
        var fixScores = new Dictionary<Guid, long>();
        foreach (var challengeId in challenges.Count == 0
                     ? activations.Select(item => item.Fact.CompetitionChallengeId!.Value).Distinct()
                     : challenges.Keys)
        {
            var settings = Effective(
                competition,
                challenges.GetValueOrDefault(challengeId)?.ConfigurationJson);
            ProjectTrack(
                challengeId,
                GameplayFactKind.BreakAttempt,
                settings.Break,
                activations,
                scoringTeams.Keys,
                dynamicTeams,
                currentRound,
                settledThroughRound,
                awards,
                breakScores);
            ProjectTrack(
                challengeId,
                GameplayFactKind.FixAttempt,
                settings.Fix,
                activations,
                scoringTeams.Keys,
                dynamicTeams,
                currentRound,
                settledThroughRound,
                awards,
                fixScores);
        }

        var awardsByTeam = awards
            .GroupBy(item => item.Fact.TeamId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var penalties = input.GameplayFacts
            .Where(fact => fact.OccurredAt <= projectedAt
                && fact.TeamId is Guid teamId
                && scoringTeams.ContainsKey(teamId)
                && fact.Kind is GameplayFactKind.BreakAttempt or GameplayFactKind.FixAttempt
                && fact.CompetitionChallengeId is Guid challengeId
                && Round(
                    fact.OccurredAt,
                    input.LifecycleAudits,
                    input.CompetitionStartTime,
                    competition.RoundDurationSeconds) <= settledThroughRound
                && (challenges.Count == 0 || challenges.ContainsKey(challengeId)))
            .GroupBy(fact => fact.TeamId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, fact) => checked(total + PenaltyFor(
                    fact,
                    Effective(
                        competition,
                        challenges.GetValueOrDefault(fact.CompetitionChallengeId!.Value)?.ConfigurationJson)))));
        var hintCosts = ProjectionPenalties.HintCosts(input, scoringTeams.Keys);
        var manualAdjustments = ProjectionPenalties.ManualAdjustments(input, scoringTeams.Keys);
        var rows = scoringTeams.Values.Select(team =>
        {
            var own = awardsByTeam.GetValueOrDefault(team.Id) ?? [];
            var last = own.Select(item => item.Fact.OccurredAt)
                .OrderByDescending(value => value)
                .FirstOrDefault();
            var lastFixAt = own
                .Where(item => item.Fact.Kind == GameplayFactKind.FixAttempt)
                .Select(item => (DateTimeOffset?)item.Fact.OccurredAt)
                .OrderByDescending(value => value)
                .FirstOrDefault();
            var awardScore = own.Aggregate(0L, (total, item) => checked(total + item.Points));
            var attackScore = own
                .Where(item => item.Fact.Kind == GameplayFactKind.BreakAttempt)
                .Aggregate(0L, (total, item) => checked(total + item.Points));
            var defenseScore = own
                .Where(item => item.Fact.Kind == GameplayFactKind.FixAttempt)
                .Aggregate(0L, (total, item) => checked(total + item.Points));
            var penalty = penalties.GetValueOrDefault(team.Id);
            return new RankedEntry(
                new LeaderboardEntry(
                    0,
                    team.Id,
                    team.Name,
                    checked(awardScore - penalty - hintCosts.GetValueOrDefault(team.Id)
                        + manualAdjustments.GetValueOrDefault(team.Id)),
                    own.Count,
                    last == default ? null : last,
                    team.TrackKey)
                {
                    AttackScore = attackScore,
                    DefenseScore = defenseScore,
                    PenaltyScore = penalty
                },
                own.Count(item => item.Fact.Kind == GameplayFactKind.FixAttempt),
                own.Count(item => item.Fact.Kind == GameplayFactKind.BreakAttempt),
                penalty,
                lastFixAt,
                team.RegisteredAt);
        });
        var entries = rows
            .GroupBy(row => row.Entry.TrackKey, StringComparer.OrdinalIgnoreCase)
            .SelectMany(track => track
                .OrderByDescending(row => row.Entry.Score)
                .ThenByDescending(row => row.FixCount)
                .ThenByDescending(row => row.BreakCount)
                .ThenBy(row => row.Penalty)
                .ThenBy(row => row.LastFixAt ?? DateTimeOffset.MaxValue)
                .ThenBy(row => row.RegisteredAt)
                .ThenBy(row => row.Entry.TeamId)
                .Select((row, index) => row.Entry with { Rank = index + 1 }))
            .ToList();
        var cells = ProjectionPenalties.ApplyManualAdjustments(input, awards
            .GroupBy(item => new
            {
                TeamId = item.Fact.TeamId!.Value,
                ChallengeId = item.Fact.CompetitionChallengeId!.Value
            })
            .Select(group =>
            {
                var first = group
                    .OrderBy(item => item.Fact.OccurredAt)
                    .ThenBy(item => item.Fact.GameplayFactId)
                    .First();
                return new LeaderboardCellFact(
                    group.Key.TeamId,
                    group.Key.ChallengeId,
                    group.Aggregate(0L, (total, item) => checked(total + item.Points)),
                    first.Fact.OccurredAt,
                    string.IsNullOrWhiteSpace(first.Fact.SubmitterName)
                        ? null
                        : first.Fact.SubmitterName)
                {
                    AttackScore = group
                        .Where(item => item.Fact.Kind == GameplayFactKind.BreakAttempt)
                        .Aggregate(0L, (total, item) => checked(total + item.Points)),
                    DefenseScore = group
                        .Where(item => item.Fact.Kind == GameplayFactKind.FixAttempt)
                        .Aggregate(0L, (total, item) => checked(total + item.Points))
                };
            })
            .ToList());
        return new(
            entries,
            cells,
            null,
            breakScores,
            fixScores,
            currentRound,
            settledThroughRound,
            competition.RoundDurationSeconds,
            currentRoundRemainingSeconds);
    }

    private static void ProjectTrack(
        Guid challengeId,
        GameplayFactKind kind,
        ScoreCurveConfiguration curve,
        IReadOnlyList<Activation> allActivations,
        IEnumerable<Guid> scoringTeamIds,
        IReadOnlySet<Guid> dynamicTeamIds,
        int currentRound,
        int settledThroughRound,
        ICollection<Award> awards,
        IDictionary<Guid, long> currentScores)
    {
        var scoringTeams = scoringTeamIds.ToHashSet();
        var activations = allActivations
            .Where(item => item.Fact.CompetitionChallengeId == challengeId && item.Fact.Kind == kind)
            .OrderBy(item => item.Round)
            .ThenBy(item => item.Fact.OccurredAt)
            .ThenBy(item => item.Fact.GameplayFactId)
            .ToList();
        var scoringActivations = activations
            .Where(item => scoringTeams.Contains(item.Fact.TeamId!.Value)
                && item.Round <= settledThroughRound)
            .ToList();
        var cumulativePoints = scoringActivations.ToDictionary(
            item => item.Fact.GameplayFactId,
            _ => 0L);
        var changeRounds = activations
            .Where(item => item.Round <= settledThroughRound
                && (dynamicTeamIds.Contains(item.Fact.TeamId!.Value)
                    || scoringTeams.Contains(item.Fact.TeamId!.Value)))
            .Select(item => item.Round)
            .Distinct()
            .Order()
            .ToArray();
        for (var index = 0; index < changeRounds.Length; index++)
        {
            var firstRound = changeRounds[index];
            var lastRound = index + 1 < changeRounds.Length
                ? changeRounds[index + 1] - 1
                : settledThroughRound;
            var roundCount = checked(lastRound - firstRound + 1);
            var dynamicCount = activations.Count(item =>
                item.Round <= firstRound
                && dynamicTeamIds.Contains(item.Fact.TeamId!.Value));
            var points = ScoreCurve.Evaluate(curve, dynamicCount, dynamicTeamIds.Count);
            var segmentPoints = checked(points * roundCount);
            foreach (var activation in scoringActivations.Where(item => item.Round <= firstRound))
                cumulativePoints[activation.Fact.GameplayFactId] = checked(
                    cumulativePoints[activation.Fact.GameplayFactId] + segmentPoints);
        }
        foreach (var activation in scoringActivations)
            awards.Add(new(
                activation.Fact,
                cumulativePoints[activation.Fact.GameplayFactId],
                activation.Round));
        var currentDynamicCount = activations.Count(item =>
            item.Round <= currentRound && dynamicTeamIds.Contains(item.Fact.TeamId!.Value));
        currentScores[challengeId] = ScoreCurve.Evaluate(
            curve,
            currentDynamicCount,
            dynamicTeamIds.Count);
    }

    private static int Round(
        DateTimeOffset occurredAt,
        IReadOnlyList<NoCTF.Domain.Competitions.CompetitionLifecycleTransition>? lifecycleAudits,
        DateTimeOffset? start,
        int durationSeconds)
    {
        if (durationSeconds <= 0)
            return 1;
        var elapsed = EffectiveElapsed(occurredAt, lifecycleAudits, start);
        return Round(elapsed, durationSeconds);
    }

    private static TimeSpan EffectiveElapsed(
        DateTimeOffset occurredAt,
        IReadOnlyList<NoCTF.Domain.Competitions.CompetitionLifecycleTransition>? lifecycleAudits,
        DateTimeOffset? start) => lifecycleAudits is not null
        ? AwdEffectiveRunningClock.Calculate(lifecycleAudits, occurredAt)
        : start is { } startedAt
            ? occurredAt - startedAt
            : TimeSpan.Zero;

    private static int Round(TimeSpan elapsed, int durationSeconds)
    {
        if (durationSeconds <= 0)
            return 1;
        var seconds = Math.Max(0, elapsed.TotalSeconds);
        return checked((int)(seconds / durationSeconds) + 1);
    }

    private static int SettledThroughRound(
        TimeSpan elapsed,
        int durationSeconds,
        bool competitionFinished)
    {
        if (durationSeconds <= 0)
            return competitionFinished ? 1 : 0;
        var seconds = Math.Max(0, elapsed.TotalSeconds);
        var completedRounds = checked((int)(seconds / durationSeconds));
        if (!competitionFinished || seconds <= completedRounds * (double)durationSeconds)
            return completedRounds;
        return checked(completedRounds + 1);
    }

    private static AwdpConfiguration ParseCompetition(string? json)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                return AwdpConfigurationParser.ParseCompetition(json);
            }
            catch (GameModeConfigurationException)
            {
            }
        }
        return new(
            AwdpConfiguration.CurrentSchemaVersion,
            300,
            ScoreCurveConfiguration.Default,
            ScoreCurveConfiguration.Default,
            RequireBreakBeforeFix: false);
    }

    private static AwdpChallengeConfiguration ParseChallenge(string? json)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                return AwdpConfigurationParser.ParseChallenge(json);
            }
            catch (GameModeConfigurationException)
            {
            }
        }
        return new(AwdpChallengeConfiguration.CurrentSchemaVersion, null, null, null, null, null);
    }

    private static AwdpEffectiveConfiguration Effective(
        AwdpConfiguration competition,
        string? challengeJson) =>
        AwdpConfigurationResolver.Resolve(competition, ParseChallenge(challengeJson));

    private static long PenaltyFor(
        LeaderboardGameplayFact fact,
        AwdpEffectiveConfiguration configuration) =>
        (fact.Kind, fact.Result, fact.FailureCode) switch
        {
            (GameplayFactKind.BreakAttempt, GameplayFactResult.Wrong, _)
                => configuration.FlagWrongPenalty,
            (GameplayFactKind.BreakAttempt, GameplayFactResult.Rejected,
                GameplayFactFailureCode.ForeignTeamFlagDetected or GameplayFactFailureCode.AmbiguousFlagMatch)
                => configuration.FlagWrongPenalty,
            (GameplayFactKind.FixAttempt, GameplayFactResult.Wrong,
                GameplayFactFailureCode.AwdpExploitSucceeded)
                => configuration.ExploitSucceededPenalty,
            (GameplayFactKind.FixAttempt, GameplayFactResult.Wrong,
                GameplayFactFailureCode.AwdpServiceAbnormal)
                => configuration.ServiceAbnormalPenalty,
            _ => 0L
        };

    private sealed record Activation(LeaderboardGameplayFact Fact, int Round);
    private sealed record Award(LeaderboardGameplayFact Fact, long Points, int ActivationRound);
    private sealed record RankedEntry(
        LeaderboardEntry Entry,
        int FixCount,
        int BreakCount,
        long Penalty,
        DateTimeOffset? LastFixAt,
        DateTimeOffset RegisteredAt);
}
