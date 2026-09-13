using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Leaderboard;

internal static class NormalizedScoreboardProjection
{
    private const int CompactEntryLimit = 5;
    private const int CompactAdjustmentLimit = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static Dictionary<Guid, ScoreboardChallengeAchievement[]> BuildAchievements(
        LeaderboardProjectionInput input, IReadOnlyDictionary<Guid, LeaderboardChallengeFact> challenges)
    {
        if (input.Mode is not (GameMode.Ctf or GameMode.Awdp)) return [];
        // All-history bounded summaries retain the first success outside the visible round window.
        var facts = input.GameplayFacts.Where(fact => fact.TeamId is not null
                && fact.CompetitionChallengeId is Guid challengeId && challenges.ContainsKey(challengeId)
                && fact.State == GameplayFactState.Completed && fact.Result == GameplayFactResult.Correct
                && fact.OccurredAt <= input.ProjectedAt
                && (input.Mode == GameMode.Ctf ? fact.Kind == GameplayFactKind.FlagAttempt
                    : fact.Kind is GameplayFactKind.BreakAttempt or GameplayFactKind.FixAttempt))
            .OrderBy(fact => fact.OccurredAt).ThenBy(fact => fact.GameplayFactId).ToArray();
        IReadOnlyList<LeaderboardGameplayFact> eligibleFacts = facts;
        if (input.Mode == GameMode.Awdp)
        {
            var configuration = ParseAwdpCompetition(input.CompetitionConfigurationJson);
            var settingsByChallenge = challenges.ToDictionary(
                pair => pair.Key,
                pair => EffectiveAwdp(configuration, pair.Value.ConfigurationJson));
            var priorBreaks = new HashSet<(Guid TeamId, Guid ChallengeId)>();
            var filtered = new List<LeaderboardGameplayFact>(facts.Length);
            foreach (var fact in facts)
            {
                var key = (fact.TeamId!.Value, fact.CompetitionChallengeId!.Value);
                if (fact.Kind != GameplayFactKind.FixAttempt
                    || !settingsByChallenge[key.Item2].RequireBreakBeforeFix
                    || priorBreaks.Contains(key))
                    filtered.Add(fact);
                if (fact.Kind == GameplayFactKind.BreakAttempt)
                    priorBreaks.Add(key);
            }
            eligibleFacts = filtered;
        }
        return eligibleFacts
            .GroupBy(fact => (fact.TeamId, fact.CompetitionChallengeId, fact.Kind))
            .Select(group => group.First()).GroupBy(fact => fact.TeamId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(fact => new ScoreboardChallengeAchievement(
                fact.CompetitionChallengeId!.Value,
                fact.Kind switch
                {
                    GameplayFactKind.FlagAttempt => ScoreboardEntryKind.Solve,
                    GameplayFactKind.BreakAttempt => ScoreboardEntryKind.Attack,
                    _ => ScoreboardEntryKind.Defense
                },
                fact.ActorUserId, fact.SubmitterName, fact.OccurredAt)).ToArray());
    }

    public static ScoreboardProjection Project(
        LeaderboardProjectionInput input,
        LeaderboardProjectionResult legacy)
    {
        var projectedAt = input.ProjectedAt
            ?? throw new InvalidOperationException("Leaderboard projection time is required.");
        var scoreboardInput = input.ScoreboardGameplayFacts is null
            ? input
            : input with { GameplayFacts = input.ScoreboardGameplayFacts };
        var challenges = (input.Challenges ?? [])
            .Where(challenge => !challenge.IsDeleted)
            .OrderBy(challenge => challenge.Order)
            .ThenBy(challenge => challenge.Id)
            .ToArray();
        var challengeById = challenges.ToDictionary(challenge => challenge.Id);
        var catalogRevision = StableRevision(challenges.Select(challenge => JsonSerializer.Serialize(new
        {
            challenge.Id,
            challenge.Title,
            challenge.Direction,
            Category = challenge.Direction,
            challenge.Order,
            challenge.IsPublished
        })));
        var catalog = new ScoreboardChallengeCatalog(
            input.CompetitionId,
            catalogRevision,
            challenges.Select(challenge => new ScoreboardChallengeCatalogItem(
                challenge.Id,
                challenge.Title,
                challenge.Direction,
                challenge.Direction,
                challenge.Order,
                challenge.IsPublished)).ToArray());

        var roundProjection = BuildRounds(scoreboardInput, legacy, projectedAt);
        var columns = BuildColumns(input.Mode, challenges, roundProjection.Rounds);
        var schemaRevision = ScoreboardRevision.ForSchema(roundProjection.Rounds, columns);
        var schema = new ScoreboardSchema(
            input.CompetitionId,
            input.Mode,
            schemaRevision,
            catalogRevision,
            roundProjection.Rounds,
            columns)
        {
            RoundWindowStart = roundProjection.WindowStart,
            RoundWindowEnd = roundProjection.WindowEnd,
            LatestRound = roundProjection.LatestRound
        };

        var actors = scoreboardInput.GameplayFacts
            .Where(fact => fact.ActorUserId is not null)
            .GroupBy(fact => fact.ActorUserId!.Value)
            .OrderBy(group => group.Key)
            .Select((group, index) => new ScoreboardActor(
                index,
                group.Key,
                group.Select(fact => fact.SubmitterName)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "-"))
            .ToArray();
        var actorIndexes = actors.ToDictionary(actor => actor.UserId, actor => actor.Index);
        var actorsByIndex = actors.ToDictionary(actor => actor.Index);
        var globalAdjustmentsByTeam = BuildGlobalAdjustments(scoreboardInput, actorIndexes);
        var achievementsByTeam = BuildAchievements(input, challengeById);
        var columnsByKey = columns.ToDictionary(
            column => (column.CompetitionChallengeId, column.RoundId),
            column => column);
        var roundById = roundProjection.Rounds.ToDictionary(round => round.Id);
        var accumulators = new Dictionary<(Guid TeamId, int ColumnIndex), SlotAccumulator>();

        switch (input.Mode)
        {
            case GameMode.Ctf:
                ProjectCtf(scoreboardInput, legacy, challengeById, columnsByKey, actorIndexes, accumulators);
                break;
            case GameMode.Awd:
                ProjectAwd(scoreboardInput, challengeById, columnsByKey, roundById, actorIndexes, accumulators);
                break;
            case GameMode.Awdp:
                ProjectAwdp(
                    scoreboardInput,
                    challengeById,
                    columnsByKey,
                    roundProjection,
                    actorIndexes,
                    accumulators);
                break;
            case GameMode.Koh:
                ProjectKoh(scoreboardInput, legacy, columnsByKey, actorIndexes, accumulators);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input), input.Mode, "Unsupported game mode.");
        }

        var legacyByTeam = legacy.Entries.ToDictionary(entry => entry.TeamId);
        var slotsByTeam = accumulators
            .GroupBy(pair => pair.Key.TeamId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(pair => pair.Key.ColumnIndex).Select(pair => pair.Value).ToArray());
        var rows = new List<ScoreboardTeam>(scoreboardInput.Teams.Count);
        var entryAllocations = new List<ScoreboardEntryAllocation>();
        foreach (var team in scoreboardInput.Teams)
        {
            var builtSlots = slotsByTeam.GetValueOrDefault(team.Id, [])
                .Select(accumulator => new
                {
                    Accumulator = accumulator,
                    Slot = accumulator.Build(ScoreState(scoreboardInput, accumulator.Round))
                })
                .Where(item => item.Slot.EntryCount > 0
                    || item.Slot.NetPoints.GetValueOrDefault() != 0
                    || item.Slot.OffenseState != ScoreboardOperationState.None
                    || item.Slot.DefenseState != ScoreboardOperationState.None)
                .ToArray();
            var compactSlots = builtSlots.Select(item => item.Slot with
            {
                Entries = CompactEntries(item.Slot.Entries)
            }).ToArray();
            entryAllocations.AddRange(builtSlots.SelectMany(item =>
                item.Accumulator.Allocate(team.Id, item.Slot)));
            var slotNet = compactSlots.Aggregate(0L, (total, slot) =>
                checked(total + slot.NetPoints.GetValueOrDefault()));
            legacyByTeam.TryGetValue(team.Id, out var legacyRow);
            var globalAdjustmentSummary = globalAdjustmentsByTeam.GetValueOrDefault(team.Id)
                ?? GlobalAdjustmentSummary.Empty;
            var allGlobalAdjustments = globalAdjustmentSummary.Adjustments;
            var globalAdjustmentNet = allGlobalAdjustments.Aggregate(0L, (total, adjustment) =>
                checked(total + adjustment.NetPoints));
            var usesRoundWindow = scoreboardInput.Mode is GameMode.Awd or GameMode.Awdp;
            var totalScore = usesRoundWindow
                ? legacyRow?.Score ?? 0
                : checked(slotNet + globalAdjustmentNet);
            var scoreOutsideWindow = usesRoundWindow
                ? checked(totalScore - slotNet - globalAdjustmentNet)
                : 0;
            var attackScore = scoreboardInput.Mode == GameMode.Awdp
                ? legacyRow?.AttackScore ?? 0
                : (long?)null;
            var defenseScore = scoreboardInput.Mode == GameMode.Awdp
                ? legacyRow?.DefenseScore ?? 0
                : (long?)null;
            var challengeScores = scoreboardInput.Mode == GameMode.Awdp && legacyRow is not null
                ? legacyRow.Cells
                    .OrderBy(cell => cell.CompetitionChallengeId)
                    .Select(cell => new ScoreboardChallengeScore(
                        cell.CompetitionChallengeId,
                        cell.AttackScore ?? 0,
                        cell.DefenseScore ?? 0))
                    .ToArray()
                : [];
            var memberContributions = builtSlots
                .SelectMany(item => item.Slot.Entries)
                .Where(entry => entry.ActorIndex is not null
                    && entry.EarnedPoints.GetValueOrDefault() > 0)
                .GroupBy(entry => entry.ActorIndex!.Value)
                .Select(group =>
                {
                    var actor = actorsByIndex[group.Key];
                    return new ScoreboardMemberContribution(
                        actor.UserId,
                        actor.DisplayName,
                        group.Aggregate(0L, (total, entry) =>
                            checked(total + entry.EarnedPoints.GetValueOrDefault())));
                })
                .OrderByDescending(contribution => contribution.EarnedPoints)
                .ThenBy(contribution => contribution.DisplayName, StringComparer.Ordinal)
                .ThenBy(contribution => contribution.UserId)
                .ToArray();
            var globalAdjustmentCount = globalAdjustmentSummary.Count;
            rows.Add(new ScoreboardTeam(
                team.Id,
                team.Name,
                team.TrackKey,
                legacyRow?.Rank,
                team.IsBanned
                    ? ScoreboardRankingState.Banned
                    : team.IsDeleted || !team.AffectsCompetitiveResults
                        ? ScoreboardRankingState.Disqualified
                        : ScoreboardRankingState.Eligible,
                totalScore,
                globalAdjustmentCount,
                CompactAdjustments(allGlobalAdjustments),
                compactSlots)
            {
                ScoreOutsideWindow = scoreOutsideWindow,
                AttackScore = attackScore,
                DefenseScore = defenseScore,
                ChallengeScores = challengeScores,
                MemberContributions = memberContributions,
                Achievements = achievementsByTeam.GetValueOrDefault(team.Id, [])
            });
        }

        var orderedRows = rows
            .OrderBy(row => row.Rank ?? int.MaxValue)
            .ThenBy(row => row.TeamId)
            .ToArray();
        var compacted = CompactActors(actors, orderedRows);
        int? MapCompactedActor(int? actorIndex) => actorIndex is int value
            && compacted.IndexMap.TryGetValue(value, out var mapped)
                ? mapped
                : null;
        var currentRoundId = roundProjection.CurrentRoundNumber is int currentRound
            ? roundProjection.Rounds.FirstOrDefault(round => round.Number == currentRound)?.Id
            : null;
        var snapshot = new ScoreboardSnapshot(
            input.CompetitionId,
            Math.Max(1, projectedAt.UtcTicks),
            schemaRevision,
            projectedAt,
            currentRoundId,
            compacted.Actors,
            compacted.Teams)
        {
            DataScope = LeaderboardDataScope.Live,
            DataAsOf = projectedAt,
            CurrentChallengeScores = legacy.Challenges
                .OrderBy(challenge => challenge.CompetitionChallengeId)
                .Select(challenge => new ScoreboardCurrentChallengeScore(
                    challenge.CompetitionChallengeId,
                    challenge.CurrentScore,
                    challenge.CurrentBreakScore,
                    challenge.CurrentFixScore))
                .ToArray()
        };
        return new(catalog, schema, snapshot)
        {
            DetailActors = compacted.Actors,
            EntryAllocations = entryAllocations
                .OrderBy(allocation => allocation.TeamId)
                .ThenBy(allocation => allocation.ColumnIndex)
                .ThenBy(allocation => allocation.Entry.OccurredAt)
                .ThenBy(allocation => allocation.Entry.Id)
                .Select(allocation => allocation with
                {
                    Entry = allocation.Entry with
                    {
                        ActorIndex = MapCompactedActor(allocation.Entry.ActorIndex)
                    }
                })
                .ToArray()
        };
    }

    private static IReadOnlyList<ScoreboardSlotEntry> CompactEntries(
        IReadOnlyList<ScoreboardSlotEntry> entries)
    {
        if (entries.Count <= CompactEntryLimit)
            return entries;

        // Totals and EntryCount remain authoritative. The main snapshot contains only
        // a small, deterministic recent summary; complete raw history is cursor-paged.
        return entries
            .OrderByDescending(entry => IsScoreBearing(entry))
            .ThenByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Take(CompactEntryLimit)
            .OrderBy(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Id)
            .ToArray();
    }

    private static bool IsScoreBearing(ScoreboardSlotEntry entry) =>
        entry.NetPoints.GetValueOrDefault() != 0
        || entry.AwardPoints != 0
        || entry.Award is not null;

    private static IReadOnlyList<ScoreboardAdjustment> CompactAdjustments(
        IReadOnlyList<ScoreboardAdjustment> adjustments) => adjustments
        .OrderByDescending(adjustment => adjustment.OccurredAt)
        .ThenByDescending(adjustment => adjustment.Id)
        .Take(CompactAdjustmentLimit)
        .OrderBy(adjustment => adjustment.OccurredAt)
        .ThenBy(adjustment => adjustment.Id)
        .ToArray();

    private static CompactedActors CompactActors(
        IReadOnlyList<ScoreboardActor> actors,
        IReadOnlyList<ScoreboardTeam> teams)
    {
        var referencedIndexes = teams
            .SelectMany(team => team.GlobalAdjustments.Select(adjustment => adjustment.ActorIndex)
                .Concat(team.Slots.SelectMany(slot => slot.Entries.Select(entry => entry.ActorIndex))))
            .Where(index => index is not null)
            .Select(index => index!.Value)
            .ToHashSet();
        var actorPairs = actors
            .Where(actor => referencedIndexes.Contains(actor.Index))
            .OrderBy(actor => actor.Index)
            .Select((actor, index) => new { OldIndex = actor.Index, Actor = actor with { Index = index } })
            .ToArray();
        var indexMap = actorPairs.ToDictionary(pair => pair.OldIndex, pair => pair.Actor.Index);
        int? MapActor(int? actorIndex) => actorIndex is int value
            && indexMap.TryGetValue(value, out var mapped)
                ? mapped
                : null;
        var compactTeams = teams.Select(team => team with
        {
            GlobalAdjustments = team.GlobalAdjustments
                .Select(adjustment => adjustment with { ActorIndex = MapActor(adjustment.ActorIndex) })
                .ToArray(),
            Slots = team.Slots.Select(slot => slot with
            {
                Entries = slot.Entries.Select(entry => entry with
                {
                    ActorIndex = MapActor(entry.ActorIndex)
                }).ToArray()
            }).ToArray()
        }).ToArray();
        return new(
            actorPairs.Select(pair => pair.Actor).ToArray(),
            compactTeams,
            indexMap);
    }

    private static IReadOnlyList<ScoreboardColumn> BuildColumns(
        GameMode mode,
        IReadOnlyList<LeaderboardChallengeFact> challenges,
        IReadOnlyList<ScoreboardRound> rounds)
    {
        var columns = new List<ScoreboardColumn>();
        foreach (var challenge in challenges)
        {
            if (mode is GameMode.Ctf or GameMode.Koh)
            {
                columns.Add(new(columns.Count, challenge.Id, null));
                continue;
            }
            foreach (var round in rounds.OrderBy(round => round.Number).ThenBy(round => round.Id))
                columns.Add(new(columns.Count, challenge.Id, round.Id));
        }
        return columns;
    }

    private static RoundProjection BuildRounds(
        LeaderboardProjectionInput input,
        LeaderboardProjectionResult legacy,
        DateTimeOffset projectedAt)
    {
        if (input.Mode is GameMode.Ctf or GameMode.Koh)
            return new([], null, null, null, null);
        if (input.Mode == GameMode.Awd)
        {
            var facts = (input.AwdRounds ?? [])
                .GroupBy(round => round.RoundId)
                .Select(group => group.OrderBy(round => round.StartsAt).ThenBy(round => round.RoundId).First())
                .OrderBy(round => round.StartsAt)
                .ThenBy(round => round.RoundId)
                .ToArray();
            var awdWindowEnd = input.ScoreboardRoundWindowEnd
                ?? input.ScoreboardLatestRound
                ?? facts.Length;
            var awdWindowStart = facts.Length == 0
                ? (int?)null
                : Math.Max(1, awdWindowEnd - facts.Length + 1);
            var rounds = facts.Select((round, index) => new ScoreboardRound(
                round.RoundId,
                awdWindowStart.GetValueOrDefault(1) + index,
                round.StartsAt,
                round.EndsAt,
                round.EndsAt <= projectedAt ? round.EndsAt : null,
                round.EndsAt <= projectedAt
                    ? ScoreboardRoundState.Settled
                    : round.StartsAt <= projectedAt
                        ? ScoreboardRoundState.Running
                        : ScoreboardRoundState.Pending))
                .ToArray();
            return new(
                rounds,
                rounds.FirstOrDefault(round => round.State == ScoreboardRoundState.Running)?.Number,
                awdWindowStart,
                facts.Length == 0 ? null : awdWindowEnd,
                input.ScoreboardLatestRound ?? (facts.Length == 0 ? null : awdWindowEnd));
        }

        var duration = legacy.RoundDurationSeconds.GetValueOrDefault(300);
        var runningTimeline = AwdEffectiveRunningClock.CreateTimeline(input.LifecycleAudits ?? []);
        var currentRound = Math.Max(1, legacy.CurrentRound.GetValueOrDefault(1));
        var settledThrough = Math.Max(0, legacy.SettledThroughRound.GetValueOrDefault());
        var lastRound = Math.Max(currentRound, settledThrough);
        var windowEnd = Math.Clamp(input.ScoreboardRoundWindowEnd ?? lastRound, 1, lastRound);
        var windowStart = Math.Max(1, windowEnd - ScoreboardRoundWindow.DefaultSize + 1);
        var roundsResult = new List<ScoreboardRound>(windowEnd - windowStart + 1);
        for (var number = windowStart; number <= windowEnd; number++)
        {
            var start = EffectiveClockToWallTime(
                input,
                runningTimeline,
                TimeSpan.FromSeconds((long)(number - 1) * duration));
            var end = number == currentRound && input.CompetitionStatus == CompetitionStatus.Paused
                ? projectedAt.AddSeconds(Math.Max(0, legacy.CurrentRoundRemainingSeconds.GetValueOrDefault()))
                : EffectiveClockToWallTime(
                    input,
                    runningTimeline,
                    TimeSpan.FromSeconds((long)number * duration));
            var settled = number <= settledThrough;
            roundsResult.Add(new(
                StableGuid(input.CompetitionId, $"round:{number}"),
                number,
                start,
                end,
                settled ? end : null,
                settled
                    ? ScoreboardRoundState.Settled
                    : number == currentRound
                        ? ScoreboardRoundState.Running
                        : ScoreboardRoundState.Pending));
        }
        return new(
            roundsResult,
            currentRound >= windowStart && currentRound <= windowEnd ? currentRound : null,
            windowStart,
            windowEnd,
            lastRound);
    }

    private static void ProjectCtf(
        LeaderboardProjectionInput input,
        LeaderboardProjectionResult legacy,
        IReadOnlyDictionary<Guid, LeaderboardChallengeFact> challenges,
        IReadOnlyDictionary<(Guid ChallengeId, Guid? RoundId), ScoreboardColumn> columns,
        IReadOnlyDictionary<Guid, int> actorIndexes,
        IDictionary<(Guid TeamId, int ColumnIndex), SlotAccumulator> slots)
    {
        var legacyCells = legacy.Entries
            .SelectMany(entry => entry.Cells.Select(cell => (entry.TeamId, Cell: cell)))
            .ToDictionary(item => (item.TeamId, item.Cell.CompetitionChallengeId), item => item.Cell);
        var factsByCell = input.GameplayFacts
            .Where(fact => fact.TeamId is not null
                && fact.CompetitionChallengeId is Guid challengeId
                && challenges.ContainsKey(challengeId))
            .GroupBy(fact => (fact.TeamId!.Value, fact.CompetitionChallengeId!.Value));
        var defaultWrongPenalty = ParseCtfCompetition(input.CompetitionConfigurationJson).WrongSubmissionPenalty;
        var challengeConfigurations = challenges.ToDictionary(
            pair => pair.Key,
            pair => ParseCtfChallenge(pair.Value.ConfigurationJson));
        var currentScores = legacy.Challenges.ToDictionary(
            challenge => challenge.CompetitionChallengeId,
            challenge => challenge.CurrentScore);

        foreach (var group in factsByCell)
        {
            var column = columns[(group.Key.Item2, null)];
            var slot = GetSlot(slots, group.Key.Item1, column, null);
            legacyCells.TryGetValue(group.Key, out var legacyCell);
            var challengeConfiguration = challengeConfigurations[group.Key.Item2];
            var wrongPenalty = challengeConfiguration.WrongSubmissionPenalty ?? defaultWrongPenalty;
            var manualTotal = group
                .Where(fact => fact.Kind == GameplayFactKind.ManualAdjustment
                    && fact.Result == GameplayFactResult.Applied)
                .Aggregate(0L, (total, fact) => checked(total
                    + ProjectionPenalties.ParseDelta(fact.Value) * fact.Multiplicity));
            var solvePoints = checked((legacyCell?.Score ?? 0) - manualTotal);
            var firstCorrect = group
                .Where(fact => fact.Kind == GameplayFactKind.FlagAttempt
                    && fact.Result == GameplayFactResult.Correct)
                .OrderBy(fact => fact.OccurredAt)
                .ThenBy(fact => fact.GameplayFactId)
                .FirstOrDefault();

            foreach (var fact in group.OrderBy(fact => fact.OccurredAt).ThenBy(fact => fact.GameplayFactId))
            {
                switch (fact.Kind)
                {
                    case GameplayFactKind.FlagAttempt:
                        {
                            var isAwardedSolve = firstCorrect?.GameplayFactId == fact.GameplayFactId;
                            var deduction = fact.Result == GameplayFactResult.Wrong
                                ? checked(wrongPenalty * fact.Multiplicity)
                                : 0L;
                            var earned = isAwardedSolve ? Math.Max(0, solvePoints) : 0L;
                            var award = isAwardedSolve ? AwardFrom(legacyCell?.BloodRank) : null;
                            var basePoints = currentScores.GetValueOrDefault(group.Key.Item2) ?? earned;
                            var awardPoints = award is null ? 0L : Math.Max(0, earned - basePoints);
                            slot.AddFact(
                                fact,
                                ScoreboardEntryKind.Solve,
                                ScoreboardBreakdownKind.Solve,
                                actorIndexes,
                                earned,
                                deduction,
                                award,
                                awardPoints,
                                Math.Max(0, earned - awardPoints),
                                deductedPointsPerOccurrence: fact.Result == GameplayFactResult.Wrong
                                    ? wrongPenalty
                                    : 0);
                            if (awardPoints > 0)
                                slot.AddBreakdownScore(ScoreboardBreakdownKind.BloodAward, awardPoints, 0);
                            break;
                        }
                    case GameplayFactKind.HintUnlock:
                        slot.AddFact(
                            fact,
                            ScoreboardEntryKind.Hint,
                            ScoreboardBreakdownKind.Hint,
                            actorIndexes,
                            0,
                            fact.Result == GameplayFactResult.Unlocked
                                ? checked(fact.HintCost.GetValueOrDefault() * fact.Multiplicity)
                                : 0,
                            deductedPointsPerOccurrence: fact.Result == GameplayFactResult.Unlocked
                                ? fact.HintCost.GetValueOrDefault()
                                : 0);
                        break;
                }
            }
        }
    }

    private static void ProjectAwd(
        LeaderboardProjectionInput input,
        IReadOnlyDictionary<Guid, LeaderboardChallengeFact> challenges,
        IReadOnlyDictionary<(Guid ChallengeId, Guid? RoundId), ScoreboardColumn> columns,
        IReadOnlyDictionary<Guid, ScoreboardRound> rounds,
        IReadOnlyDictionary<Guid, int> actorIndexes,
        IDictionary<(Guid TeamId, int ColumnIndex), SlotAccumulator> slots)
    {
        var competition = ParseAwdCompetition(input.CompetitionConfigurationJson);
        var settingsByChallenge = challenges.ToDictionary(
            pair => pair.Key,
            pair => EffectiveAwd(competition, pair.Value.ConfigurationJson));
        var validTeams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted && team.EarnsScore)
            .Select(team => team.Id)
            .ToHashSet();
        var competitiveTeams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted && team.AffectsCompetitiveResults)
            .Select(team => team.Id)
            .ToHashSet();
        var attackFacts = input.GameplayFacts
            .Where(fact => fact.TeamId is Guid teamId
                && competitiveTeams.Contains(teamId)
                && fact.Kind == GameplayFactKind.FlagAttempt
                && fact.Result == GameplayFactResult.Correct
                && fact.ReferenceKind == GameplayFactReferenceKind.AwdRound
                && fact.ReferenceId is Guid roundId
                && rounds.ContainsKey(roundId)
                && fact.CompetitionChallengeId is Guid challengeId
                && challenges.ContainsKey(challengeId)
                && fact.VictimTeamId is Guid victimId
                && victimId != teamId
                && competitiveTeams.Contains(victimId))
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.GameplayFactId)
            .GroupBy(fact => new
            {
                ChallengeId = fact.CompetitionChallengeId!.Value,
                RoundId = fact.ReferenceId!.Value,
                VictimId = fact.VictimTeamId!.Value,
                AttackerId = fact.TeamId!.Value
            })
            .Select(group => group.First())
            .ToArray();
        var awardedAttackIds = attackFacts
            .Select(fact => fact.GameplayFactId)
            .ToHashSet();

        foreach (var victimPool in attackFacts.GroupBy(fact => new
        {
            ChallengeId = fact.CompetitionChallengeId!.Value,
            RoundId = fact.ReferenceId!.Value,
            VictimId = fact.VictimTeamId!.Value
        }))
        {
            var settings = settingsByChallenge[victimPool.Key.ChallengeId];
            var attackers = victimPool.Select(fact => fact.TeamId!.Value).Distinct().ToArray();
            var reward = settings.AttackRewardMode == AttackRewardMode.FixedPerAttack
                ? settings.AttackPoints
                : settings.VictimDefensePoolPoints / attackers.Length;
            var column = columns[(victimPool.Key.ChallengeId, victimPool.Key.RoundId)];
            var round = rounds[victimPool.Key.RoundId];
            foreach (var fact in victimPool)
            {
                if (!validTeams.Contains(fact.TeamId!.Value))
                    continue;
                GetSlot(slots, fact.TeamId.Value, column, round).AddFact(
                    fact,
                    ScoreboardEntryKind.Attack,
                    ScoreboardBreakdownKind.Attack,
                    actorIndexes,
                    reward,
                    0);
            }
            if (validTeams.Contains(victimPool.Key.VictimId))
            {
                var victimSlot = GetSlot(slots, victimPool.Key.VictimId, column, round);
                victimSlot.AddSystemAttempt(
                    StableGuid(victimPool.Key.RoundId, $"defense:{victimPool.Key.ChallengeId:N}:{victimPool.Key.VictimId:N}"),
                    ScoreboardEntryKind.Defense,
                    ScoreboardBreakdownKind.Defense,
                    ScoreboardEntryOutcome.Failed,
                    round.EndAt,
                    0,
                    settings.VictimDefensePoolPoints);
            }
        }

        var serviceStates = input.GameplayFacts
            .Where(fact => fact.Kind == GameplayFactKind.AwdServiceTransition
                && fact.TeamId is not null
                && fact.CompetitionChallengeId is not null
                && fact.Result is GameplayFactResult.ServiceUp or GameplayFactResult.ServiceDown)
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.GameplayFactId)
            .GroupBy(fact => (fact.TeamId!.Value, fact.CompetitionChallengeId!.Value))
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var roundFact in (input.AwdRounds ?? [])
                     .Where(item => validTeams.Contains(item.TeamId)
                         && challenges.ContainsKey(item.CompetitionChallengeId))
                     .GroupBy(item => (item.TeamId, item.CompetitionChallengeId, item.RoundId))
                     .Select(group => group.First()))
        {
            if (!rounds.TryGetValue(roundFact.RoundId, out var round)
                || round.State != ScoreboardRoundState.Settled)
                continue;
            var settings = settingsByChallenge[roundFact.CompetitionChallengeId];
            var latest = serviceStates.TryGetValue(
                (roundFact.TeamId, roundFact.CompetitionChallengeId),
                out var transitions)
                ? LatestBefore(transitions, roundFact.EndsAt)
                : null;
            var up = latest?.Result != GameplayFactResult.ServiceDown;
            var slot = GetSlot(
                slots,
                roundFact.TeamId,
                columns[(roundFact.CompetitionChallengeId, roundFact.RoundId)],
                round);
            slot.AddSystemAttempt(
                StableGuid(roundFact.RoundId,
                    $"availability:{roundFact.CompetitionChallengeId:N}:{roundFact.TeamId:N}"),
                ScoreboardEntryKind.Availability,
                ScoreboardBreakdownKind.Availability,
                up ? ScoreboardEntryOutcome.Succeeded : ScoreboardEntryOutcome.Failed,
                round.EndAt,
                up ? settings.ServiceHealthyPoints : 0,
                up ? 0 : settings.ServiceUnhealthyPenalty);
        }

        foreach (var fact in input.GameplayFacts
                     .Where(fact => fact.TeamId is Guid teamId
                         && validTeams.Contains(teamId)
                         && fact.CompetitionChallengeId is Guid challengeId
                         && challenges.ContainsKey(challengeId))
                     .OrderBy(fact => fact.OccurredAt)
                     .ThenBy(fact => fact.GameplayFactId))
        {
            if (fact.ReferenceKind != GameplayFactReferenceKind.AwdRound
                || fact.ReferenceId is not Guid roundId
                || !rounds.TryGetValue(roundId, out var round))
                continue;
            var column = columns[(fact.CompetitionChallengeId!.Value, roundId)];
            var slot = GetSlot(slots, fact.TeamId!.Value, column, round);
            if (fact.Kind == GameplayFactKind.FlagAttempt
                && !awardedAttackIds.Contains(fact.GameplayFactId))
            {
                slot.AddFact(
                    fact,
                    ScoreboardEntryKind.Attack,
                    ScoreboardBreakdownKind.Attack,
                    actorIndexes,
                    0,
                    0);
            }
        }
    }

    private static void ProjectAwdp(
        LeaderboardProjectionInput input,
        IReadOnlyDictionary<Guid, LeaderboardChallengeFact> challenges,
        IReadOnlyDictionary<(Guid ChallengeId, Guid? RoundId), ScoreboardColumn> columns,
        RoundProjection roundProjection,
        IReadOnlyDictionary<Guid, int> actorIndexes,
        IDictionary<(Guid TeamId, int ColumnIndex), SlotAccumulator> slots)
    {
        var competition = ParseAwdpCompetition(input.CompetitionConfigurationJson);
        var settingsByChallenge = challenges.ToDictionary(
            pair => pair.Key,
            pair => EffectiveAwdp(competition, pair.Value.ConfigurationJson));
        var runningTimeline = AwdEffectiveRunningClock.CreateTimeline(input.LifecycleAudits ?? []);
        var activeTeams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted)
            .ToDictionary(team => team.Id);
        var scoringTeams = activeTeams.Values.Where(team => team.EarnsScore).Select(team => team.Id).ToHashSet();
        var competitiveTeams = activeTeams.Values
            .Where(team => team.AffectsCompetitiveResults)
            .Select(team => team.Id)
            .ToHashSet();
        var correctFacts = input.GameplayFacts
            .Where(fact => fact.TeamId is Guid teamId
                && activeTeams.ContainsKey(teamId)
                && fact.CompetitionChallengeId is Guid challengeId
                && challenges.ContainsKey(challengeId)
                && fact.Kind is GameplayFactKind.BreakAttempt or GameplayFactKind.FixAttempt
                && fact.Result == GameplayFactResult.Correct
                && (fact.Kind != GameplayFactKind.BreakAttempt
                    || competitiveTeams.Contains(teamId)
                    && (fact.VictimTeamId is null || competitiveTeams.Contains(fact.VictimTeamId.Value))))
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.GameplayFactId)
            .ToArray();
        var priorBreaks = new HashSet<(Guid TeamId, Guid ChallengeId)>();
        var activationKeys = new HashSet<(Guid TeamId, Guid ChallengeId, GameplayFactKind Kind)>();
        var activationList = new List<AwdpActivation>();
        foreach (var fact in correctFacts)
        {
            var teamId = fact.TeamId!.Value;
            var challengeId = fact.CompetitionChallengeId!.Value;
            var achievementKey = (teamId, challengeId);
            if (fact.Kind == GameplayFactKind.FixAttempt
                && settingsByChallenge[challengeId].RequireBreakBeforeFix
                && !priorBreaks.Contains(achievementKey))
                continue;
            if (activationKeys.Add((teamId, challengeId, fact.Kind)))
                activationList.Add(new(
                    fact,
                    FactRound(input, runningTimeline, fact, competition.RoundDurationSeconds)));
            if (fact.Kind == GameplayFactKind.BreakAttempt)
                priorBreaks.Add(achievementKey);
        }
        var activations = activationList.ToArray();
        var activationsByTrack = activations
            .GroupBy(item => (
                ChallengeId: item.Fact.CompetitionChallengeId!.Value,
                item.Fact.Kind))
            .ToDictionary(group => group.Key, group => group
                .OrderBy(item => item.Round)
                .ThenBy(item => item.Fact.OccurredAt)
                .ThenBy(item => item.Fact.GameplayFactId)
                .ToArray());
        var curveEvaluator = new Scoring.ScoreCurveEvaluator();

        foreach (var activation in activations)
        {
            foreach (var round in roundProjection.Rounds.Where(round => round.Number >= activation.Round))
            {
                var slot = GetSlot(
                    slots,
                    activation.Fact.TeamId!.Value,
                    columns[(activation.Fact.CompetitionChallengeId!.Value, round.Id)],
                    round);
                if (activation.Fact.Kind == GameplayFactKind.BreakAttempt)
                    slot.MarkOffenseSucceeded();
                else
                    slot.MarkDefenseSucceeded();
            }
        }

        var settledRounds = roundProjection.Rounds
            .Where(round => round.State == ScoreboardRoundState.Settled)
            .OrderBy(round => round.Number)
            .ThenBy(round => round.Id)
            .ToArray();
        foreach (var challenge in challenges.Values)
        {
            var effective = settingsByChallenge[challenge.Id];
            foreach (var kind in new[] { GameplayFactKind.BreakAttempt, GameplayFactKind.FixAttempt })
            {
                var trackActivations = activationsByTrack.GetValueOrDefault((challenge.Id, kind)) ?? [];
                var breakdownKind = kind == GameplayFactKind.BreakAttempt
                    ? ScoreboardBreakdownKind.Attack
                    : ScoreboardBreakdownKind.Defense;
                var curve = kind == GameplayFactKind.BreakAttempt ? effective.Break : effective.Fix;
                var nextActivation = 0;
                var successfulTeamCount = 0;
                var activeScoring = new List<AwdpActivation>(trackActivations.Length);
                foreach (var round in settledRounds)
                {
                    while (nextActivation < trackActivations.Length
                           && trackActivations[nextActivation].Round <= round.Number)
                    {
                        var activation = trackActivations[nextActivation++];
                        var teamId = activation.Fact.TeamId!.Value;
                        if (competitiveTeams.Contains(teamId))
                            successfulTeamCount++;
                        if (scoringTeams.Contains(teamId))
                            activeScoring.Add(activation);
                    }
                    if (successfulTeamCount == 0)
                        continue;
                    var points = curveEvaluator.Evaluate(
                        curve,
                        successfulTeamCount,
                        competitiveTeams.Count);
                    foreach (var activation in activeScoring)
                    {
                        var slot = GetSlot(
                            slots,
                            activation.Fact.TeamId!.Value,
                            columns[(challenge.Id, round.Id)],
                            round);
                        slot.AddSettlement(
                            StableGuid(
                                activation.Fact.GameplayFactId,
                                $"awdp-settlement:{round.Id:N}:{kind}"),
                            kind == GameplayFactKind.BreakAttempt
                                ? ScoreboardEntryKind.Attack
                                : ScoreboardEntryKind.Defense,
                            breakdownKind,
                            activation.Fact,
                            actorIndexes,
                            points,
                            0);
                    }
                }
            }
        }

        var roundsByNumber = roundProjection.Rounds.ToDictionary(round => round.Number);
        foreach (var fact in input.GameplayFacts
                     .Where(fact => fact.TeamId is Guid teamId
                         && scoringTeams.Contains(teamId)
                         && fact.CompetitionChallengeId is Guid challengeId
                         && challenges.ContainsKey(challengeId)
                         && fact.Kind is GameplayFactKind.BreakAttempt or GameplayFactKind.FixAttempt)
                     .OrderBy(fact => fact.OccurredAt)
                     .ThenBy(fact => fact.GameplayFactId))
        {
            var roundNumber = FactRound(input, runningTimeline, fact, competition.RoundDurationSeconds);
            if (!roundsByNumber.TryGetValue(roundNumber, out var round))
                continue;
            var kind = fact.Kind == GameplayFactKind.BreakAttempt
                ? ScoreboardEntryKind.Attack
                : ScoreboardEntryKind.Defense;
            var breakdown = fact.Kind == GameplayFactKind.BreakAttempt
                ? ScoreboardBreakdownKind.Attack
                : ScoreboardBreakdownKind.Defense;
            var penalty = AwdpPenalty(
                fact,
                settingsByChallenge[fact.CompetitionChallengeId!.Value]);
            GetSlot(
                    slots,
                    fact.TeamId!.Value,
                    columns[(fact.CompetitionChallengeId.Value, round.Id)],
                    round)
                .AddFact(
                    fact,
                    kind,
                    breakdown,
                    actorIndexes,
                    0,
                    penalty,
                    deductedPointsPerOccurrence: fact.Multiplicity > 0
                        ? penalty / fact.Multiplicity
                        : 0);
        }
    }

    private static void ProjectKoh(
        LeaderboardProjectionInput input,
        LeaderboardProjectionResult legacy,
        IReadOnlyDictionary<(Guid ChallengeId, Guid? RoundId), ScoreboardColumn> columns,
        IReadOnlyDictionary<Guid, int> actorIndexes,
        IDictionary<(Guid TeamId, int ColumnIndex), SlotAccumulator> slots)
    {
        var legacyCells = legacy.Entries
            .SelectMany(entry => entry.Cells.Select(cell => (entry.TeamId, Cell: cell)))
            .ToDictionary(item => (item.TeamId, item.Cell.CompetitionChallengeId), item => item.Cell);
        foreach (var group in input.GameplayFacts
                     .Where(fact => fact.TeamId is not null && fact.CompetitionChallengeId is not null)
                     .GroupBy(fact => (fact.TeamId!.Value, fact.CompetitionChallengeId!.Value)))
        {
            if (!columns.TryGetValue((group.Key.Item2, null), out var column))
                continue;
            var slot = GetSlot(slots, group.Key.Item1, column, null);
            var manual = 0L;
            foreach (var fact in group.OrderBy(fact => fact.OccurredAt).ThenBy(fact => fact.GameplayFactId))
            {
                if (fact.Kind == GameplayFactKind.KohControlObservation)
                {
                    slot.AddFact(
                        fact,
                        ScoreboardEntryKind.Control,
                        ScoreboardBreakdownKind.Control,
                        actorIndexes,
                        0,
                        0);
                }
                else if (fact.Kind == GameplayFactKind.ManualAdjustment
                         && fact.Result == GameplayFactResult.Applied)
                    manual = checked(manual
                        + ProjectionPenalties.ParseDelta(fact.Value) * fact.Multiplicity);
            }
            var control = checked((legacyCells.GetValueOrDefault(group.Key)?.Score ?? 0L) - manual);
            slot.AddBreakdownScore(
                ScoreboardBreakdownKind.Control,
                Math.Max(0, control),
                Math.Max(0, -control));
        }
    }

    private static ScoreboardScoreState ScoreState(
        LeaderboardProjectionInput input,
        ScoreboardRound? round)
    {
        if (input.Mode == GameMode.Awdp && round?.State != ScoreboardRoundState.Settled)
            return ScoreboardScoreState.Pending;
        if (round?.State == ScoreboardRoundState.Pending)
            return ScoreboardScoreState.Pending;
        if (round?.State == ScoreboardRoundState.Settled
            || input.CompetitionStatus == CompetitionStatus.Finished)
            return ScoreboardScoreState.Settled;
        return ScoreboardScoreState.Provisional;
    }

    private static SlotAccumulator GetSlot(
        IDictionary<(Guid TeamId, int ColumnIndex), SlotAccumulator> slots,
        Guid teamId,
        ScoreboardColumn column,
        ScoreboardRound? round)
    {
        var key = (teamId, column.Index);
        if (!slots.TryGetValue(key, out var slot))
        {
            slot = new SlotAccumulator(column.Index, round);
            slots.Add(key, slot);
        }
        return slot;
    }

    private static IReadOnlyDictionary<Guid, GlobalAdjustmentSummary> BuildGlobalAdjustments(
        LeaderboardProjectionInput input,
        IReadOnlyDictionary<Guid, int> actorIndexes)
    {
        var adjustments = new Dictionary<Guid, List<ScoreboardAdjustment>>();
        var counts = new Dictionary<Guid, int>();
        foreach (var fact in input.GameplayFacts)
        {
            if (fact.TeamId is not Guid teamId
                || fact.Kind != GameplayFactKind.ManualAdjustment
                || fact.Result != GameplayFactResult.Applied)
                continue;
            if (!adjustments.TryGetValue(teamId, out var teamAdjustments))
                adjustments.Add(teamId, teamAdjustments = []);
            var net = checked(ProjectionPenalties.ParseDelta(fact.Value) * fact.Multiplicity);
            teamAdjustments.Add(new(
                    fact.GameplayFactId,
                    ScoreboardAdjustmentKind.ManualAdjustment,
                    fact.OccurredAt,
                    fact.ActorUserId is Guid actorId && actorIndexes.TryGetValue(actorId, out var actorIndex)
                        ? actorIndex
                        : null,
                    Math.Max(0, net),
                    Math.Max(0, -net),
                    net));
            counts[teamId] = checked(counts.GetValueOrDefault(teamId) + fact.Multiplicity);
        }
        return adjustments.ToDictionary(
            pair => pair.Key,
            pair => new GlobalAdjustmentSummary(pair.Value, counts[pair.Key]));
    }

    private static DateTimeOffset EffectiveClockToWallTime(
        LeaderboardProjectionInput input,
        AwdEffectiveRunningTimeline runningTimeline,
        TimeSpan target) => runningTimeline.ToWallTime(
            input.CompetitionStartTime.GetValueOrDefault(), target);

    private static int FactRound(
        LeaderboardProjectionInput input,
        AwdEffectiveRunningTimeline runningTimeline,
        LeaderboardGameplayFact fact,
        int duration)
    {
        var elapsed = runningTimeline.HasTransitions
            ? runningTimeline.Calculate(fact.OccurredAt)
            : fact.OccurredAt - input.CompetitionStartTime.GetValueOrDefault();
        return checked((int)(Math.Max(0, elapsed.TotalSeconds) / Math.Max(1, duration)) + 1);
    }

    private static LeaderboardGameplayFact? LatestBefore(
        IReadOnlyList<LeaderboardGameplayFact> transitions,
        DateTimeOffset cutoff)
    {
        var low = 0;
        var high = transitions.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (transitions[middle].OccurredAt < cutoff)
                low = middle + 1;
            else
                high = middle;
        }
        return low == 0 ? null : transitions[low - 1];
    }

    private static ScoreboardAward? AwardFrom(LeaderboardBloodRank? rank) => rank switch
    {
        LeaderboardBloodRank.First => ScoreboardAward.FirstBlood,
        LeaderboardBloodRank.Second => ScoreboardAward.SecondBlood,
        LeaderboardBloodRank.Third => ScoreboardAward.ThirdBlood,
        _ => null
    };

    private static Ctf.Configuration.CtfConfiguration ParseCtfCompetition(string? json) =>
        TryParse<Ctf.Configuration.CtfConfiguration>(json)
        ?? new(
            Ctf.Configuration.CtfConfiguration.CurrentSchemaVersion,
            Scoring.ScoreCurveConfiguration.Default,
            []);

    private static Ctf.Configuration.CtfChallengeConfiguration ParseCtfChallenge(string? json) =>
        TryParse<Ctf.Configuration.CtfChallengeConfiguration>(json)
        ?? new(Ctf.Configuration.CtfChallengeConfiguration.CurrentSchemaVersion, null, null);

    private static AwdConfiguration ParseAwdCompetition(string? json) =>
        TryParse<AwdConfiguration>(json) ?? AwdConfiguration.Default;

    private static AwdScoringSettings EffectiveAwd(AwdConfiguration competition, string? challengeJson)
    {
        var challenge = TryParse<AwdChallengeConfiguration>(challengeJson);
        return new(
            challenge?.AttackRewardMode ?? competition.AttackRewardMode,
            challenge?.AttackPoints ?? competition.AttackPoints,
            challenge?.VictimDefensePoolPoints ?? competition.VictimDefensePoolPoints,
            challenge?.ServiceHealthyPoints ?? competition.ServiceHealthyPoints,
            challenge?.ServiceUnhealthyPenalty ?? competition.ServiceUnhealthyPenalty);
    }

    private static AwdpConfiguration ParseAwdpCompetition(string? json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json)
                ? new(
                    AwdpConfiguration.CurrentSchemaVersion,
                    300,
                    Scoring.ScoreCurveConfiguration.Default,
                    Scoring.ScoreCurveConfiguration.Default)
                : AwdpConfigurationParser.ParseCompetition(json);
        }
        catch (GameModeConfigurationException)
        {
            return new(
                AwdpConfiguration.CurrentSchemaVersion,
                300,
                Scoring.ScoreCurveConfiguration.Default,
                Scoring.ScoreCurveConfiguration.Default);
        }
    }

    private static AwdpEffectiveConfiguration EffectiveAwdp(
        AwdpConfiguration competition,
        string? challengeJson)
    {
        AwdpChallengeConfiguration challenge;
        try
        {
            challenge = string.IsNullOrWhiteSpace(challengeJson)
                ? new(AwdpChallengeConfiguration.CurrentSchemaVersion, null, null, null, null, null)
                : AwdpConfigurationParser.ParseChallenge(challengeJson);
        }
        catch (GameModeConfigurationException)
        {
            challenge = new(AwdpChallengeConfiguration.CurrentSchemaVersion, null, null, null, null, null);
        }
        return AwdpConfigurationResolver.Resolve(competition, challenge);
    }

    private static long AwdpPenalty(
        LeaderboardGameplayFact fact,
        AwdpEffectiveConfiguration configuration) => (fact.Kind, fact.Result, fact.FailureCode) switch
        {
            (GameplayFactKind.BreakAttempt, GameplayFactResult.Wrong, _)
                => checked(configuration.FlagWrongPenalty * fact.Multiplicity),
            (GameplayFactKind.BreakAttempt, GameplayFactResult.Rejected,
                GameplayFactFailureCode.ForeignTeamFlagDetected or GameplayFactFailureCode.AmbiguousFlagMatch)
                => checked(configuration.FlagWrongPenalty * fact.Multiplicity),
            (GameplayFactKind.FixAttempt, GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpExploitSucceeded)
                => checked(configuration.ExploitSucceededPenalty * fact.Multiplicity),
            (GameplayFactKind.FixAttempt, GameplayFactResult.Rejected, GameplayFactFailureCode.AwdpServiceAbnormal)
                => checked(configuration.ServiceAbnormalPenalty * fact.Multiplicity),
            _ => 0
        };

    private static T? TryParse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ScoreboardEntryOutcome EntryOutcome(LeaderboardGameplayFact fact)
    {
        if (fact.State is GameplayFactState.Queued or GameplayFactState.Processing || fact.Result is null)
            return ScoreboardEntryOutcome.Pending;
        return fact.Result switch
        {
            GameplayFactResult.Correct or GameplayFactResult.Unlocked or GameplayFactResult.Applied
                or GameplayFactResult.ServiceUp or GameplayFactResult.Controlled
                => ScoreboardEntryOutcome.Succeeded,
            GameplayFactResult.Wrong or GameplayFactResult.AttemptsExhausted
                or GameplayFactResult.ServiceDown or GameplayFactResult.Uncontrolled
                => ScoreboardEntryOutcome.Failed,
            _ => ScoreboardEntryOutcome.Rejected
        };
    }

    private static long StableRevision(IEnumerable<string> values)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', values)));
        return Math.Max(1, BitConverter.ToInt64(bytes, 0) & long.MaxValue);
    }

    private static Guid StableGuid(Guid scope, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope:N}|{value}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private sealed class SlotAccumulator
    {
        private readonly Dictionary<ScoreboardBreakdownKind, BreakdownAccumulator> breakdowns = [];
        private readonly List<ScoreboardSlotEntry> entries = [];
        private readonly Dictionary<Guid, ScoreboardEntrySource> sources = [];

        private int entryCount;
        private ScoreboardOperationState offenseState;
        private ScoreboardOperationState defenseState;

        private readonly int columnIndex;

        public SlotAccumulator(int columnIndex, ScoreboardRound? round)
        {
            this.columnIndex = columnIndex;
            Round = round;
        }

        public ScoreboardRound? Round { get; }

        public void AddFact(
            LeaderboardGameplayFact fact,
            ScoreboardEntryKind entryKind,
            ScoreboardBreakdownKind breakdownKind,
            IReadOnlyDictionary<Guid, int> actorIndexes,
            long earned,
            long deducted,
            ScoreboardAward? award = null,
            long awardPoints = 0,
            long? breakdownEarned = null,
            long? earnedPointsPerOccurrence = null,
            long? deductedPointsPerOccurrence = null)
        {
            var outcome = EntryOutcome(fact);
            entryCount = checked(entryCount + fact.Multiplicity);
            AddAttempt(breakdownKind, outcome == ScoreboardEntryOutcome.Succeeded, fact.Multiplicity);
            AddBreakdownScore(breakdownKind, breakdownEarned ?? earned, deducted);
            entries.Add(new(
                fact.GameplayFactId,
                entryKind,
                outcome,
                fact.ActorUserId is Guid actorId && actorIndexes.TryGetValue(actorId, out var actorIndex)
                    ? actorIndex
                    : null,
                fact.VictimTeamId,
                fact.OccurredAt,
                Round?.SettledAt,
                earned,
                deducted,
                checked(earned - deducted),
                award,
                awardPoints));
            sources.Add(fact.GameplayFactId, new(
                fact.Kind,
                fact.State,
                fact.Result,
                fact.FailureCode,
                fact.ReferenceKind,
                fact.ReferenceId,
                fact.VictimTeamId,
                fact.ActorUserId,
                fact.Multiplicity,
                earnedPointsPerOccurrence,
                deductedPointsPerOccurrence));
        }

        public void AddSystemAttempt(
            Guid id,
            ScoreboardEntryKind entryKind,
            ScoreboardBreakdownKind breakdownKind,
            ScoreboardEntryOutcome outcome,
            DateTimeOffset occurredAt,
            long earned,
            long deducted)
        {
            entryCount++;
            AddAttempt(breakdownKind, outcome == ScoreboardEntryOutcome.Succeeded, 1);
            AddBreakdownScore(breakdownKind, earned, deducted);
            entries.Add(new(
                id,
                entryKind,
                outcome,
                null,
                null,
                occurredAt,
                Round?.SettledAt,
                earned,
                deducted,
                checked(earned - deducted)));
        }

        public void AddSettlement(
            Guid id,
            ScoreboardEntryKind entryKind,
            ScoreboardBreakdownKind breakdownKind,
            LeaderboardGameplayFact source,
            IReadOnlyDictionary<Guid, int> actorIndexes,
            long earned,
            long deducted)
        {
            entryCount++;
            AddBreakdownScore(breakdownKind, earned, deducted);
            entries.Add(new(
                id,
                entryKind,
                ScoreboardEntryOutcome.Succeeded,
                source.ActorUserId is Guid actorId && actorIndexes.TryGetValue(actorId, out var actorIndex)
                    ? actorIndex
                    : null,
                source.VictimTeamId,
                source.OccurredAt,
                Round?.SettledAt,
                earned,
                deducted,
                checked(earned - deducted)));
        }

        public void AddBreakdownScore(ScoreboardBreakdownKind kind, long earned, long deducted)
        {
            var breakdown = GetBreakdown(kind);
            breakdown.Earned = checked(breakdown.Earned + earned);
            breakdown.Deducted = checked(breakdown.Deducted + deducted);
        }

        private void AddAttempt(ScoreboardBreakdownKind kind, bool succeeded, int count)
        {
            var breakdown = GetBreakdown(kind);
            breakdown.AttemptCount = checked(breakdown.AttemptCount + count);
            if (succeeded)
                breakdown.SuccessfulCount = checked(breakdown.SuccessfulCount + count);
            var state = succeeded ? ScoreboardOperationState.Succeeded : ScoreboardOperationState.Failed;
            if (kind is ScoreboardBreakdownKind.Solve
                or ScoreboardBreakdownKind.Attack
                or ScoreboardBreakdownKind.Control)
                offenseState = MergeOperationState(offenseState, state);
            else if (kind is ScoreboardBreakdownKind.Defense or ScoreboardBreakdownKind.Availability)
                defenseState = MergeOperationState(defenseState, state);
        }

        public void MarkOffenseSucceeded() => offenseState = ScoreboardOperationState.Succeeded;

        public void MarkDefenseSucceeded() => defenseState = ScoreboardOperationState.Succeeded;

        private static ScoreboardOperationState MergeOperationState(
            ScoreboardOperationState current,
            ScoreboardOperationState candidate) =>
            current == ScoreboardOperationState.Succeeded || candidate == ScoreboardOperationState.Succeeded
                ? ScoreboardOperationState.Succeeded
                : candidate;

        private BreakdownAccumulator GetBreakdown(ScoreboardBreakdownKind kind)
        {
            if (!breakdowns.TryGetValue(kind, out var breakdown))
            {
                breakdown = new();
                breakdowns.Add(kind, breakdown);
            }
            return breakdown;
        }

        public IReadOnlyList<ScoreboardEntryAllocation> Allocate(Guid teamId, ScoreboardSlot slot)
        {
            var allocations = slot.Entries.Select(entry => new ScoreboardEntryAllocation(
                teamId,
                columnIndex,
                entry)
            {
                Source = sources.GetValueOrDefault(entry.Id)
            }).ToArray();
            var synthetic = allocations.Where(allocation => allocation.Source is null);
            var factGroups = allocations
                .Where(allocation => allocation.Source is not null)
                .GroupBy(allocation => AllocationIdentity.From(allocation))
                .Select(group => group
                    .OrderBy(allocation => allocation.Entry.OccurredAt)
                    .ThenBy(allocation => allocation.Entry.Id)
                    .First())
                .Select(allocation => allocation with
                {
                    Entry = allocation.Entry with { ActorIndex = null },
                    Source = allocation.Source! with { ActorUserId = null }
                });
            return synthetic.Concat(factGroups)
                .OrderBy(allocation => allocation.Entry.OccurredAt)
                .ThenBy(allocation => allocation.Entry.Id)
                .ToArray();
        }

        public ScoreboardSlot Build(ScoreboardScoreState state)
        {
            var projectedBreakdowns = breakdowns
                .OrderBy(pair => pair.Key)
                .Select(pair => new ScoreboardBreakdown(
                    pair.Key,
                    pair.Value.SuccessfulCount,
                    pair.Value.AttemptCount,
                    state == ScoreboardScoreState.Pending ? 0 : pair.Value.Earned,
                    state == ScoreboardScoreState.Pending ? 0 : pair.Value.Deducted,
                    state == ScoreboardScoreState.Pending
                        ? 0
                        : checked(pair.Value.Earned - pair.Value.Deducted)))
                .ToArray();
            var earned = state == ScoreboardScoreState.Pending
                ? (long?)null
                : projectedBreakdowns.Aggregate(0L, (total, item) => checked(total + item.EarnedPoints));
            var deducted = state == ScoreboardScoreState.Pending
                ? (long?)null
                : projectedBreakdowns.Aggregate(0L, (total, item) => checked(total + item.DeductedPoints));
            var projectedEntries = entries.Select(entry => state == ScoreboardScoreState.Pending
                ? entry with
                {
                    SettledAt = null,
                    EarnedPoints = null,
                    DeductedPoints = null,
                    NetPoints = null,
                    AwardPoints = 0
                }
                : entry).ToArray();
            return new(
                columnIndex,
                state,
                earned,
                deducted,
                earned is null ? null : checked(earned.Value - deducted!.Value),
                entryCount,
                projectedBreakdowns,
                projectedEntries)
            {
                OffenseState = offenseState,
                DefenseState = defenseState
            };
        }
    }

    private sealed class BreakdownAccumulator
    {
        public int SuccessfulCount { get; set; }
        public int AttemptCount { get; set; }
        public long Earned { get; set; }
        public long Deducted { get; set; }
    }

    private sealed record GlobalAdjustmentSummary(
        IReadOnlyList<ScoreboardAdjustment> Adjustments,
        int Count)
    {
        public static GlobalAdjustmentSummary Empty { get; } = new([], 0);
    }

    private sealed record RoundProjection(
        IReadOnlyList<ScoreboardRound> Rounds,
        int? CurrentRoundNumber,
        int? WindowStart,
        int? WindowEnd,
        int? LatestRound);

    private sealed record CompactedActors(
        IReadOnlyList<ScoreboardActor> Actors,
        IReadOnlyList<ScoreboardTeam> Teams,
        IReadOnlyDictionary<int, int> IndexMap);

    private sealed record AllocationIdentity(
        ScoreboardEntryKind EntryKind,
        ScoreboardEntryOutcome Outcome,
        ScoreboardAward? Award,
        long AwardPoints,
        GameplayFactKind Kind,
        GameplayFactState State,
        GameplayFactResult? Result,
        GameplayFactFailureCode? FailureCode,
        GameplayFactReferenceKind? ReferenceKind,
        Guid? ReferenceId,
        Guid? VictimTeamId,
        long? EarnedPointsPerOccurrence,
        long? DeductedPointsPerOccurrence)
    {
        public static AllocationIdentity From(ScoreboardEntryAllocation allocation)
        {
            var source = allocation.Source!;
            return new(
                allocation.Entry.Kind,
                allocation.Entry.Outcome,
                allocation.Entry.Award,
                allocation.Entry.AwardPoints,
                source.Kind,
                source.State,
                source.Result,
                source.FailureCode,
                source.ReferenceKind,
                source.ReferenceId,
                source.VictimTeamId,
                source.EarnedPointsPerOccurrence,
                source.DeductedPointsPerOccurrence);
        }
    }

    private sealed record AwdScoringSettings(
        AttackRewardMode AttackRewardMode,
        long AttackPoints,
        long VictimDefensePoolPoints,
        long ServiceHealthyPoints,
        long ServiceUnhealthyPenalty);

    private sealed record AwdpActivation(LeaderboardGameplayFact Fact, int Round);
}
