using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.GameModes.Leaderboard;

public sealed class LeaderboardProjectionEngine(ILeaderboardProjectorCatalog projectors)
    : ILeaderboardProjectionEngine
{
    public LeaderboardProjectionResult Project(LeaderboardProjectionInput input)
    {
        var entries = projectors.Get(input.Mode).Project(input);
        var observations = BuildObservations(input);
        var bloods = BuildBloods(input, observations);
        var bloodsByTeamAndSlot = bloods.ToDictionary(
            item => (item.TeamId, item.SlotKey),
            item => item);
        var subjects = entries.Select(entry =>
        {
            var slots = observations
                .Where(item => item.TeamId == entry.TeamId)
                .GroupBy(item => new { item.SlotKey, item.Kind, item.Label })
                .Select(group =>
                {
                    var blood = BloodAt(
                        bloodsByTeamAndSlot,
                        entry.TeamId,
                        group.Key.SlotKey);
                    return new LeaderboardSlotSummary(
                        group.Key.SlotKey,
                        group.Key.Kind,
                        group.Key.Label,
                        group.Count(item => item.Succeeded),
                        group.Max(item => (DateTimeOffset?)item.OccurredAt),
                        blood?.BloodRank,
                        blood?.OccurredAt);
                })
                .OrderBy(slot => slot.Kind)
                .ThenBy(slot => slot.SlotKey, StringComparer.Ordinal)
                .ToList();
            return new LeaderboardSubjectSummary(
                entry.TeamId,
                entry.TeamName,
                entry.Score,
                slots.Sum(slot => slot.SuccessCount),
                slots);
        }).ToList();
        return new(entries, subjects, bloods);
    }

    private static LeaderboardBloodSummary? BloodAt(
        IReadOnlyDictionary<(Guid TeamId, string SlotKey), LeaderboardBloodSummary> bloods,
        Guid teamId,
        string slotKey) =>
        bloods.GetValueOrDefault((teamId, slotKey));

    private static IReadOnlyList<SlotObservation> BuildObservations(LeaderboardProjectionInput input)
    {
        var validTeams = input.Teams.Where(team => !team.IsBanned && !team.IsDeleted)
            .Select(team => team.Id).ToHashSet();
        var challenges = (input.Challenges ?? []).Where(challenge => !challenge.IsDeleted)
            .ToDictionary(challenge => challenge.Id);
        var observations = new List<SlotObservation>();
        foreach (var fact in input.Submissions
                     .Where(fact => validTeams.Contains(fact.TeamId) && fact.Event.DeletedAt is null)
                     .OrderBy(fact => fact.ReceivedAt)
                     .ThenBy(fact => fact.SubmissionId))
        {
            if (fact.CompetitionChallengeId is Guid challengeId && challenges.Count > 0 && !challenges.ContainsKey(challengeId))
                continue;
            var observation = SubmissionObservation(input.Mode, fact, challenges);
            if (observation is not null) observations.Add(observation);
        }
        foreach (var fact in input.SystemEvents
                     .Where(fact => fact.Event.DeletedAt is null && fact.Event.TeamId is Guid teamId && validTeams.Contains(teamId))
                     .OrderBy(fact => fact.Event.OccurredAt)
                     .ThenBy(fact => fact.Event.Id))
        {
            var observation = SystemObservation(input.Mode, fact.Event, challenges);
            if (observation is not null) observations.Add(observation);
        }
        return observations;
    }

    private static SlotObservation? SubmissionObservation(
        GameMode mode,
        LeaderboardSubmissionFact fact,
        IReadOnlyDictionary<Guid, LeaderboardChallengeFact> challenges)
    {
        var succeeded = fact.Event.Result == ScoringResult.Correct;
        var challengeId = fact.CompetitionChallengeId;
        var challengeLabel = challengeId is Guid id && challenges.TryGetValue(id, out var challenge)
            ? challenge.Direction
            : string.Empty;
        return mode switch
        {
            GameMode.Ctf when fact.Kind == SubmissionKind.Flag && challengeId is not null =>
                Slot(fact.TeamId, $"challenge:{challengeId:N}", LeaderboardSlotKind.Challenge, challengeLabel, fact.ReceivedAt, fact.SubmissionId, succeeded, true),
            GameMode.Awd when fact.Kind == SubmissionKind.Flag =>
                Slot(fact.TeamId, $"service:{challengeId}", LeaderboardSlotKind.Service, challengeLabel, fact.ReceivedAt, fact.SubmissionId, succeeded, false),
            GameMode.Awdp when fact.Kind == SubmissionKind.Break && challengeId is not null =>
                Slot(fact.TeamId, $"break:{challengeId:N}", LeaderboardSlotKind.Break, challengeLabel, fact.ReceivedAt, fact.SubmissionId, succeeded, false),
            GameMode.Awdp when fact.Kind == SubmissionKind.Fix && challengeId is not null =>
                Slot(fact.TeamId, $"fix:{challengeId:N}", LeaderboardSlotKind.Fix, challengeLabel, fact.ReceivedAt, fact.SubmissionId, succeeded, false),
            _ => null
        };
    }

    private static SlotObservation? SystemObservation(
        GameMode mode,
        ScoringEvent scoringEvent,
        IReadOnlyDictionary<Guid, LeaderboardChallengeFact> challenges)
    {
        var teamId = scoringEvent.TeamId!.Value;
        var challengeId = scoringEvent.CompetitionChallengeId;
        var label = challengeId is Guid id && challenges.TryGetValue(id, out var challenge)
            ? challenge.Direction
            : string.Empty;
        return mode switch
        {
            GameMode.Awd when scoringEvent.Kind == ScoringEventKind.AwdServiceStatus =>
                Slot(teamId, $"service:{challengeId}", LeaderboardSlotKind.Service, label, scoringEvent.OccurredAt,
                    scoringEvent.Id, scoringEvent.Result == ScoringResult.Correct, false),
            GameMode.Koh when scoringEvent.Kind == ScoringEventKind.KohObservation =>
                Slot(teamId, $"control:{challengeId}", LeaderboardSlotKind.Control, label, scoringEvent.OccurredAt,
                    scoringEvent.Id, scoringEvent.Result == ScoringResult.Correct, false),
            _ => null
        };
    }

    private static IReadOnlyList<LeaderboardBloodSummary> BuildBloods(
        LeaderboardProjectionInput input,
        IReadOnlyList<SlotObservation> observations)
    {
        var names = input.Teams.ToDictionary(team => team.Id, team => team.Name);
        return observations.Where(item => item.BloodEligible && item.Succeeded)
            .GroupBy(item => new { item.SlotKey, item.Kind })
            .SelectMany(group => group
                .GroupBy(item => item.TeamId)
                .Select(team => team
                    .OrderBy(item => item.OccurredAt)
                    .ThenBy(item => item.StableId)
                    .First())
                .OrderBy(item => item.OccurredAt)
                .ThenBy(item => item.StableId)
                .Take(3)
                .Select((item, index) => new LeaderboardBloodSummary(
                    item.SlotKey,
                    item.Kind,
                    (LeaderboardBloodRank)(index + 1),
                    item.TeamId,
                    names.GetValueOrDefault(item.TeamId) ?? string.Empty,
                    item.OccurredAt)))
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.SlotKey, StringComparer.Ordinal)
            .ThenBy(item => item.BloodRank)
            .ToList();
    }

    private static SlotObservation Slot(
        Guid teamId,
        string key,
        LeaderboardSlotKind kind,
        string label,
        DateTimeOffset occurredAt,
        Guid stableId,
        bool succeeded,
        bool bloodEligible) =>
        new(teamId, key, kind, label, occurredAt, stableId, succeeded, bloodEligible);

    private sealed record SlotObservation(
        Guid TeamId,
        string SlotKey,
        LeaderboardSlotKind Kind,
        string Label,
        DateTimeOffset OccurredAt,
        Guid StableId,
        bool Succeeded,
        bool BloodEligible);
}
