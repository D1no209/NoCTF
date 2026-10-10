using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
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
        var competition = ParseCompetition(input.CompetitionConfiguration);
        var activeTeams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted)
            .ToDictionary(team => team.Id);
        var scoringTeams = activeTeams.Values
            .Where(team => team.EarnsScore)
            .ToDictionary(team => team.Id);
        var competitiveTeams = activeTeams.Values
            .Where(team => team.AffectsCompetitiveResults)
            .Select(team => team.Id)
            .ToHashSet();
        var challenges = (input.Challenges ?? [])
            .Where(challenge => !challenge.IsDeleted)
            .ToDictionary(challenge => challenge.Id);
        var settingsByChallenge = challenges.ToDictionary(
            pair => pair.Key,
            pair => Effective(competition, pair.Value.Rules));
        var defaultSettings = Effective(competition, null);
        AwdpEffectiveConfiguration SettingsFor(Guid challengeId) =>
            settingsByChallenge.GetValueOrDefault(challengeId) ?? defaultSettings;
        var projectedAt = input.ProjectedAt
            ?? throw new InvalidOperationException("Leaderboard projection time is required.");
        var runningTimeline = AwdEffectiveRunningClock.CreateTimeline(input.LifecycleAudits ?? []);
        var elapsed = EffectiveElapsed(
            projectedAt,
            runningTimeline,
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
        var priorBreaks = new HashSet<(Guid TeamId, Guid ChallengeId)>();
        var activationKeys = new HashSet<(Guid TeamId, Guid ChallengeId, GameplayFactKind Kind)>();
        var activations = new List<Activation>();
        foreach (var fact in correctFacts)
        {
            var teamId = fact.TeamId!.Value;
            var challengeId = fact.CompetitionChallengeId!.Value;
            var achievementKey = (teamId, challengeId);
            if (fact.Kind == GameplayFactKind.FixAttempt
                && SettingsFor(challengeId).RequireBreakBeforeFix
                && !priorBreaks.Contains(achievementKey))
                continue;
            if (activationKeys.Add((teamId, challengeId, fact.Kind)))
                activations.Add(new(
                    fact,
                    Round(
                        fact.OccurredAt,
                        runningTimeline,
                        input.CompetitionStartTime,
                        competition.RoundDurationSeconds)));
            if (fact.Kind == GameplayFactKind.BreakAttempt)
                priorBreaks.Add(achievementKey);
        }

        var awards = new List<Award>();
        var breakScores = new Dictionary<Guid, long>();
        var fixScores = new Dictionary<Guid, long>();
        var scoringTeamIds = scoringTeams.Keys.ToHashSet();
        var activationsByTrack = activations
            .GroupBy(item => (
                ChallengeId: item.Fact.CompetitionChallengeId!.Value,
                item.Fact.Kind))
            .ToDictionary(group => group.Key, group => group
                .OrderBy(item => item.Round)
                .ThenBy(item => item.Fact.OccurredAt)
                .ThenBy(item => item.Fact.GameplayFactId)
                .ToArray());
        foreach (var challengeId in challenges.Count == 0
                     ? activations.Select(item => item.Fact.CompetitionChallengeId!.Value).Distinct()
                     : challenges.Keys)
        {
            var settings = SettingsFor(challengeId);
            var challengeSettledThrough = settledThroughRound;
            if (challenges.GetValueOrDefault(challengeId)?.Timing?.ScoringEndsAt is { } scoringEnd)
                challengeSettledThrough = Math.Min(challengeSettledThrough,
                    competition.RoundDurationSeconds > 0
                        ? (int)(Math.Max(0, EffectiveElapsed(scoringEnd, runningTimeline, input.CompetitionStartTime).TotalSeconds) / competition.RoundDurationSeconds)
                        : 0);
            ProjectTrack(
                challengeId,
                settings.Break,
                activationsByTrack.GetValueOrDefault(
                    (challengeId, GameplayFactKind.BreakAttempt), []),
                scoringTeamIds,
                competitiveTeams,
                currentRound,
                challengeSettledThrough,
                awards,
                breakScores);
            ProjectTrack(
                challengeId,
                settings.Fix,
                activationsByTrack.GetValueOrDefault(
                    (challengeId, GameplayFactKind.FixAttempt), []),
                scoringTeamIds,
                competitiveTeams,
                currentRound,
                challengeSettledThrough,
                awards,
                fixScores);
        }

        var awardsByTeam = awards
            .GroupBy(item => item.Fact.TeamId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var challengePenalties = input.GameplayFacts
            .Where(fact => fact.OccurredAt <= projectedAt
                && fact.TeamId is Guid teamId
                && scoringTeams.ContainsKey(teamId)
                && fact.Kind is GameplayFactKind.BreakAttempt or GameplayFactKind.FixAttempt
                && fact.CompetitionChallengeId is Guid challengeId
                && Round(
                    fact.OccurredAt,
                    runningTimeline,
                    input.CompetitionStartTime,
                    competition.RoundDurationSeconds) <= settledThroughRound
                && (challenges.Count == 0 || challenges.ContainsKey(challengeId)))
            .GroupBy(fact => (TeamId: fact.TeamId!.Value, CompetitionChallengeId: fact.CompetitionChallengeId!.Value))
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, fact) => checked(total + PenaltyFor(
                    fact,
                    SettingsFor(fact.CompetitionChallengeId!.Value))
                    * fact.Multiplicity)));
        var penalties = challengePenalties.GroupBy(pair => pair.Key.TeamId)
            .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value));
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
                    PenaltyScore = penalty,
                    AttackCount = own.Count(item => item.Fact.Kind == GameplayFactKind.BreakAttempt),
                    FixCount = own.Count(item => item.Fact.Kind == GameplayFactKind.FixAttempt),
                    LastFixAt = lastFixAt
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
            currentRoundRemainingSeconds)
        {
            ChallengeNetScores = ProjectionPenalties.ChallengeNetScores(input, cells, challengePenalties)
        };
    }

    private static void ProjectTrack(
        Guid challengeId,
        ScoreCurveConfiguration curve,
        IReadOnlyList<Activation> activations,
        IReadOnlySet<Guid> scoringTeamIds,
        IReadOnlySet<Guid> competitiveTeamIds,
        int currentRound,
        int settledThroughRound,
        ICollection<Award> awards,
        IDictionary<Guid, long> currentScores)
    {
        var scoringActivations = activations
            .Where(item => scoringTeamIds.Contains(item.Fact.TeamId!.Value)
                && item.Round <= settledThroughRound)
            .ToArray();
        var changeRounds = activations
            .Where(item => item.Round <= settledThroughRound
                && (competitiveTeamIds.Contains(item.Fact.TeamId!.Value)
                    || scoringTeamIds.Contains(item.Fact.TeamId!.Value)))
            .Select(item => item.Round)
            .Distinct()
            .Order()
            .ToArray();
        var segments = new (int FirstRound, long Points)[changeRounds.Length];
        var activationIndex = 0;
        var successfulTeamCount = 0;
        for (var index = 0; index < changeRounds.Length; index++)
        {
            var firstRound = changeRounds[index];
            var lastRound = index + 1 < changeRounds.Length
                ? changeRounds[index + 1] - 1
                : settledThroughRound;
            var roundCount = checked(lastRound - firstRound + 1);
            while (activationIndex < activations.Count
                   && activations[activationIndex].Round <= firstRound)
            {
                if (competitiveTeamIds.Contains(
                        activations[activationIndex].Fact.TeamId!.Value))
                    successfulTeamCount++;
                activationIndex++;
            }
            var points = ScoreCurve.Evaluate(
                curve,
                successfulTeamCount,
                competitiveTeamIds.Count);
            segments[index] = (firstRound, checked(points * roundCount));
        }
        var pointsFromRound = new Dictionary<int, long>(segments.Length);
        var cumulativePoints = 0L;
        for (var index = segments.Length - 1; index >= 0; index--)
        {
            cumulativePoints = checked(cumulativePoints + segments[index].Points);
            pointsFromRound[segments[index].FirstRound] = cumulativePoints;
        }
        foreach (var activation in scoringActivations)
            awards.Add(new(
                activation.Fact,
                pointsFromRound[activation.Round],
                activation.Round));
        var currentSuccessfulTeamCount = activations.Count(item =>
            item.Round <= currentRound && competitiveTeamIds.Contains(item.Fact.TeamId!.Value));
        currentScores[challengeId] = ScoreCurve.Evaluate(
            curve,
            currentSuccessfulTeamCount,
            competitiveTeamIds.Count);
    }

    private static int Round(
        DateTimeOffset occurredAt,
        AwdEffectiveRunningTimeline runningTimeline,
        DateTimeOffset? start,
        int durationSeconds)
    {
        if (durationSeconds <= 0)
            return 1;
        var elapsed = EffectiveElapsed(occurredAt, runningTimeline, start);
        return Round(elapsed, durationSeconds);
    }

    private static TimeSpan EffectiveElapsed(
        DateTimeOffset occurredAt,
        AwdEffectiveRunningTimeline runningTimeline,
        DateTimeOffset? start) => runningTimeline.HasTransitions
        ? runningTimeline.Calculate(occurredAt)
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

    private static AwdpConfiguration ParseCompetition(
        CompetitionModeConfiguration? configuration) =>
        configuration is AwdpCompetitionModeConfiguration awdp
            ? TypedGameModeConfiguration.Awdp(awdp)
            : TypedGameModeConfiguration.Awdp(
                (AwdpCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(
                    GameMode.Awdp, Guid.Empty));

    private static AwdpChallengeConfiguration ParseChallenge(
        CompetitionChallengeRules? rules) => rules is AwdpCompetitionChallengeRules awdp
        ? TypedGameModeConfiguration.Awdp(awdp)
        : new(null, null, null, null, null);

    private static AwdpEffectiveConfiguration Effective(
        AwdpConfiguration competition,
        CompetitionChallengeRules? rules) =>
        AwdpConfigurationResolver.Resolve(
            competition,
            ParseChallenge(rules),
            AwdpChallengeConfiguration.Empty);

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
            (GameplayFactKind.FixAttempt, GameplayFactResult.Rejected,
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
