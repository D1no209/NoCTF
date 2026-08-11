using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.AdjudicationPreview;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.AdjudicationPreview;

public sealed class HistoricalAdjudicationPreviewStore(NoCtfDbContext db)
    : IHistoricalAdjudicationEvidenceStore
{
    private static readonly CompetitionEventKind[] BloodKinds =
    [
        CompetitionEventKind.FirstBloodAwarded,
        CompetitionEventKind.SecondBloodAwarded,
        CompetitionEventKind.ThirdBloodAwarded
    ];

    public async Task<HistoricalAdjudicationEvidencePage> ReadAsync(
        Guid competitionId,
        Guid? competitionChallengeId,
        DateTimeOffset? beforeOccurredAt,
        Guid? beforeId,
        int scanLimit,
        CancellationToken ct)
    {
        var mode = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => (GameMode?)competition.Mode)
            .SingleOrDefaultAsync(ct);
        if (mode is null)
            return new(null, []);

        var query = db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == competitionId
                && (fact.Kind == GameplayFactKind.FlagAttempt
                    || fact.Kind == GameplayFactKind.BreakAttempt));
        if (competitionChallengeId is Guid challengeId)
            query = query.Where(fact => fact.CompetitionChallengeId == challengeId);
        if (beforeOccurredAt is DateTimeOffset occurredAt && beforeId is Guid id)
        {
            query = query.Where(fact => fact.OccurredAt < occurredAt
                || fact.OccurredAt == occurredAt && fact.Id.CompareTo(id) < 0);
        }

        var facts = await query
            .OrderByDescending(fact => fact.OccurredAt)
            .ThenByDescending(fact => fact.Id)
            .Take(scanLimit)
            .Select(fact => new FactCandidate(
                fact.Id,
                fact.CompetitionChallengeId,
                fact.TeamId,
                fact.Kind,
                fact.Result,
                fact.OccurredAt,
                db.GameplayFacts.Any(prior =>
                    prior.CompetitionId == fact.CompetitionId
                    && prior.CompetitionChallengeId == fact.CompetitionChallengeId
                    && prior.TeamId == fact.TeamId
                    && prior.Kind == fact.Kind
                    && prior.Result == GameplayFactResult.Correct
                    && (prior.OccurredAt < fact.OccurredAt
                        || prior.OccurredAt == fact.OccurredAt
                        && prior.Id.CompareTo(fact.Id) < 0)),
                mode == GameMode.Ctf && fact.Kind == GameplayFactKind.FlagAttempt
                    ? db.GameplayFacts
                        .Where(prior =>
                            prior.CompetitionId == fact.CompetitionId
                            && prior.CompetitionChallengeId == fact.CompetitionChallengeId
                            && prior.Kind == GameplayFactKind.FlagAttempt
                            && prior.Result == GameplayFactResult.Correct
                            && prior.TeamId != null
                            && prior.TeamId != fact.TeamId
                            && (prior.OccurredAt < fact.OccurredAt
                                || prior.OccurredAt == fact.OccurredAt
                                && prior.Id.CompareTo(fact.Id) < 0))
                        .Select(prior => prior.TeamId)
                        .Distinct()
                        .Count()
                    : 0))
            .ToArrayAsync(ct);
        if (facts.Length == 0)
            return new(mode, []);

        var factIds = facts.Select(fact => fact.Id).ToArray();
        var eventGroups = await db.CompetitionEvents.AsNoTracking()
            .Where(@event => @event.CompetitionId == competitionId
                && @event.SubjectType == EntityReferenceKind.GameplayFact
                && factIds.Contains(@event.SubjectId)
                && (@event.Kind == CompetitionEventKind.GameplayFactAdjudicated
                    || BloodKinds.Contains(@event.Kind)))
            .GroupBy(@event => new { @event.SubjectId, @event.Kind, @event.PayloadJson })
            .Select(group => new EventGroup(
                group.Key.SubjectId,
                group.Key.Kind,
                group.Key.PayloadJson,
                group.Count()))
            .ToArrayAsync(ct);

        var teamIds = facts.Where(fact => fact.TeamId is not null)
            .Select(fact => fact.TeamId!.Value).Distinct().ToArray();
        var teamNames = await db.Teams.AsNoTracking()
            .Where(team => teamIds.Contains(team.Id))
            .ToDictionaryAsync(team => team.Id, team => team.Name, ct);
        var challengeIds = facts.Select(fact => fact.CompetitionChallengeId).Distinct().ToArray();
        var challengeTitles = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challengeIds.Contains(challenge.Id))
            .Join(
                db.Challenges.AsNoTracking(),
                competitionChallenge => competitionChallenge.ChallengeId,
                challenge => challenge.Id,
                (competitionChallenge, challenge) => new { competitionChallenge.Id, challenge.Title })
            .ToDictionaryAsync(item => item.Id, item => item.Title, ct);

        var evidence = facts.Select(fact =>
        {
            var factEvents = eventGroups.Where(group => group.GameplayFactId == fact.Id).ToArray();
            var historicalResults = factEvents
                .Where(group => group.Kind == CompetitionEventKind.GameplayFactAdjudicated)
                .Select(group => ParseResult(group.PayloadJson))
                .Where(result => result is not null)
                .Select(result => result!.Value)
                .Distinct()
                .ToArray();
            var recordedBloodRanks = factEvents
                .Where(group => BloodKinds.Contains(group.Kind))
                .SelectMany(group => Enumerable.Repeat(ToBloodRank(group.Kind), group.Count))
                .Order()
                .ToArray();
            return new HistoricalAdjudicationEvidence(
                fact.Id,
                fact.CompetitionChallengeId,
                challengeTitles.GetValueOrDefault(fact.CompetitionChallengeId, fact.CompetitionChallengeId.ToString()),
                fact.TeamId,
                fact.TeamId is Guid teamId ? teamNames.GetValueOrDefault(teamId, teamId.ToString()) : null,
                fact.Kind,
                fact.Result,
                fact.OccurredAt,
                fact.HasEarlierCorrect,
                fact.EarlierCorrectTeamCount,
                historicalResults,
                recordedBloodRanks);
        }).ToArray();
        return new(mode, evidence);
    }

    private static LeaderboardBloodRank ToBloodRank(CompetitionEventKind kind) => kind switch
    {
        CompetitionEventKind.FirstBloodAwarded => LeaderboardBloodRank.First,
        CompetitionEventKind.SecondBloodAwarded => LeaderboardBloodRank.Second,
        CompetitionEventKind.ThirdBloodAwarded => LeaderboardBloodRank.Third,
        _ => throw new InvalidOperationException("Competition event is not a blood award.")
    };

    private static GameplayFactResult? ParseResult(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (!document.RootElement.TryGetProperty("gameplayFactResult", out var result)
                || result.ValueKind != JsonValueKind.String)
                return null;
            return Enum.TryParse<GameplayFactResult>(result.GetString(), out var parsed)
                ? parsed
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record FactCandidate(
        Guid Id,
        Guid CompetitionChallengeId,
        Guid? TeamId,
        GameplayFactKind Kind,
        GameplayFactResult? Result,
        DateTimeOffset OccurredAt,
        bool HasEarlierCorrect,
        int EarlierCorrectTeamCount);

    private sealed record EventGroup(
        Guid GameplayFactId,
        CompetitionEventKind Kind,
        string PayloadJson,
        int Count);
}
