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
        var firstBloods = BuildFirstBloods(input, observations);
        var firstBloodTimes = firstBloods.ToDictionary(
            item => (item.TeamId, item.SlotKey),
            item => item.OccurredAt);
        var subjects = entries.Select(entry =>
        {
            var slots = observations
                .Where(item => item.TeamId == entry.TeamId)
                .GroupBy(item => new { item.SlotKey, item.Kind, item.Label })
                .Select(group => new LeaderboardSlotSummary(
                    group.Key.SlotKey,
                    group.Key.Kind,
                    group.Key.Label,
                    group.Count(item => item.Succeeded),
                    group.Max(item => (DateTimeOffset?)item.OccurredAt),
                    FirstBloodAt(firstBloodTimes, entry.TeamId, group.Key.SlotKey)))
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
        return new(entries, subjects, firstBloods);
    }

    private static DateTimeOffset? FirstBloodAt(
        IReadOnlyDictionary<(Guid TeamId, string SlotKey), DateTimeOffset> firstBloodTimes,
        Guid teamId,
        string slotKey) =>
        firstBloodTimes.TryGetValue((teamId, slotKey), out var occurredAt) ? occurredAt : null;

    private static IReadOnlyList<SlotObservation> BuildObservations(LeaderboardProjectionInput input)
    {
        var validTeams = input.Teams.Where(team => !team.IsBanned && !team.IsDeleted)
            .Select(team => team.Id).ToHashSet();
        var challenges = (input.Challenges ?? []).Where(challenge => !challenge.IsDeleted)
            .ToDictionary(challenge => challenge.Id);
        var observations = new List<SlotObservation>();
        foreach (var fact in input.Submissions
                     .Where(fact => validTeams.Contains(fact.TeamId) && !fact.Event.IsDeleted)
                     .OrderBy(fact => fact.ReceivedAt)
                     .ThenBy(fact => fact.SubmissionId))
        {
            if (fact.ChallengeId is Guid challengeId && challenges.Count > 0 && !challenges.ContainsKey(challengeId))
                continue;
            var observation = SubmissionObservation(input.Mode, fact, challenges);
            if (observation is not null) observations.Add(observation);
        }
        foreach (var fact in input.SystemEvents
                     .Where(fact => !fact.Event.IsDeleted && fact.Event.TeamId is Guid teamId && validTeams.Contains(teamId))
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
        var challengeId = fact.ChallengeId;
        var challengeLabel = challengeId is Guid id && challenges.TryGetValue(id, out var challenge)
            ? challenge.Direction
            : string.Empty;
        return mode switch
        {
            GameMode.Ctf when fact.Kind == SubmissionKind.Flag && challengeId is not null =>
                Slot(fact.TeamId, $"challenge:{challengeId:N}", LeaderboardSlotKind.Challenge, challengeLabel, fact.ReceivedAt, fact.SubmissionId, succeeded, true),
            GameMode.Awd when fact.Kind == SubmissionKind.Flag =>
                Slot(fact.TeamId, $"service:{fact.ServiceId ?? challengeId}", LeaderboardSlotKind.Service, challengeLabel, fact.ReceivedAt, fact.SubmissionId, succeeded, false),
            GameMode.Awdp when fact.Kind == SubmissionKind.Flag && challengeId is not null =>
                Slot(fact.TeamId, $"break:{challengeId:N}", LeaderboardSlotKind.Break, challengeLabel, fact.ReceivedAt, fact.SubmissionId, succeeded, false),
            GameMode.Awdp when fact.Kind == SubmissionKind.Fix && challengeId is not null =>
                Slot(fact.TeamId, $"fix:{challengeId:N}", LeaderboardSlotKind.Fix, challengeLabel, fact.ReceivedAt, fact.SubmissionId, succeeded, false),
            GameMode.Penetration when fact.StageId is Guid stageId =>
                Slot(fact.TeamId, $"stage:{stageId:N}", LeaderboardSlotKind.Stage, challengeLabel, fact.ReceivedAt, fact.SubmissionId, succeeded, true),
            _ => null
        };
    }

    private static SlotObservation? SystemObservation(
        GameMode mode,
        ScoringEvent scoringEvent,
        IReadOnlyDictionary<Guid, LeaderboardChallengeFact> challenges)
    {
        var teamId = scoringEvent.TeamId!.Value;
        var challengeId = scoringEvent.ChallengeId;
        var label = challengeId is Guid id && challenges.TryGetValue(id, out var challenge)
            ? challenge.Direction
            : string.Empty;
        return mode switch
        {
            GameMode.Awd when scoringEvent.Kind == ScoringEventKind.AwdServiceCheck =>
                Slot(teamId, $"service:{challengeId}", LeaderboardSlotKind.Service, label, scoringEvent.OccurredAt,
                    scoringEvent.Id, scoringEvent.Result == ScoringResult.Correct, false),
            GameMode.Awdp when scoringEvent.Kind == ScoringEventKind.AwdServiceCheck =>
                Slot(teamId, $"service:{challengeId}", LeaderboardSlotKind.Service, label, scoringEvent.OccurredAt,
                    scoringEvent.Id, scoringEvent.Result == ScoringResult.Correct, false),
            GameMode.Koh when scoringEvent.Kind == ScoringEventKind.KohObservation =>
                Slot(teamId, $"control:{challengeId}", LeaderboardSlotKind.Control, label, scoringEvent.OccurredAt,
                    scoringEvent.Id, scoringEvent.Result == ScoringResult.Correct, false),
            _ => null
        };
    }

    private static IReadOnlyList<LeaderboardFirstBloodSummary> BuildFirstBloods(
        LeaderboardProjectionInput input,
        IReadOnlyList<SlotObservation> observations)
    {
        var names = input.Teams.ToDictionary(team => team.Id, team => team.Name);
        return observations.Where(item => item.FirstBloodEligible && item.Succeeded)
            .GroupBy(item => new { item.SlotKey, item.Kind })
            .Select(group => group.OrderBy(item => item.OccurredAt).ThenBy(item => item.StableId).First())
            .Select(item => new LeaderboardFirstBloodSummary(
                item.SlotKey,
                item.Kind,
                item.TeamId,
                names.GetValueOrDefault(item.TeamId) ?? string.Empty,
                item.OccurredAt))
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.SlotKey, StringComparer.Ordinal)
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
        bool firstBloodEligible) =>
        new(teamId, key, kind, label, occurredAt, stableId, succeeded, firstBloodEligible);

    private sealed record SlotObservation(
        Guid TeamId,
        string SlotKey,
        LeaderboardSlotKind Kind,
        string Label,
        DateTimeOffset OccurredAt,
        Guid StableId,
        bool Succeeded,
        bool FirstBloodEligible);
}
