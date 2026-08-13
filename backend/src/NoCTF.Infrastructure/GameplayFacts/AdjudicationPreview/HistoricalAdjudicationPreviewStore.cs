using System.Text.Json;
using System.Data;
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
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            ct);
        var competition = await db.Competitions.IgnoreQueryFilters().AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => new { competition.Mode })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
        {
            await transaction.CommitAsync(ct);
            return new(HistoricalAdjudicationPreviewReadState.CompetitionNotFound, []);
        }
        var gameplayFactKind = competition.Mode switch
        {
            GameMode.Ctf => GameplayFactKind.FlagAttempt,
            GameMode.Awdp => GameplayFactKind.BreakAttempt,
            _ => (GameplayFactKind?)null
        };
        if (gameplayFactKind is null)
        {
            await transaction.CommitAsync(ct);
            return new(HistoricalAdjudicationPreviewReadState.Available, []);
        }

        var query = db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == competitionId
                && fact.Kind == gameplayFactKind.Value);
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
                fact.FailureCode,
                fact.OccurredAt))
            .ToArrayAsync(ct);
        if (facts.Length == 0)
        {
            await transaction.CommitAsync(ct);
            return new(HistoricalAdjudicationPreviewReadState.Available, []);
        }

        var challengeIds = facts.Select(fact => fact.CompetitionChallengeId).Distinct().ToArray();
        FirstCorrectFact[] firstCorrects = competition.Mode == GameMode.Ctf
            ? await db.GameplayFacts.AsNoTracking()
                .Where(fact => fact.CompetitionId == competitionId
                    && challengeIds.Contains(fact.CompetitionChallengeId)
                    && fact.TeamId != null
                    && fact.Kind == GameplayFactKind.FlagAttempt
                    && fact.Result == GameplayFactResult.Correct)
                .GroupBy(fact => new { fact.CompetitionChallengeId, fact.TeamId })
                .Select(group => group
                    .OrderBy(fact => fact.OccurredAt)
                    .ThenBy(fact => fact.Id)
                    .Select(fact => new FirstCorrectFact(
                        fact.Id,
                        fact.CompetitionChallengeId,
                        fact.TeamId!.Value,
                        fact.OccurredAt))
                    .First())
                .ToArrayAsync(ct)
            : [];

        var factIds = facts.Select(fact => fact.Id).ToArray();
        var includeBloodAwards = competition.Mode == GameMode.Ctf;
        var eventGroups = await db.CompetitionEvents.AsNoTracking()
            .Where(@event => @event.CompetitionId == competitionId
                && @event.SubjectType == EntityReferenceKind.GameplayFact
                && factIds.Contains(@event.SubjectId)
                && (@event.Kind == CompetitionEventKind.GameplayFactAdjudicated
                    || includeBloodAwards && BloodKinds.Contains(@event.Kind)))
            .GroupBy(@event => new { @event.SubjectId, @event.Kind, @event.PayloadJson })
            .Select(group => new EventGroup(
                group.Key.SubjectId,
                group.Key.Kind,
                group.Key.PayloadJson,
                group.Count()))
            .ToArrayAsync(ct);

        var teamIds = facts.Where(fact => fact.TeamId is not null)
            .Select(fact => fact.TeamId!.Value)
            .Concat(firstCorrects.Select(fact => fact.TeamId))
            .Distinct()
            .ToArray();
        var teams = await db.Teams.IgnoreQueryFilters().AsNoTracking()
            .Where(team => teamIds.Contains(team.Id))
            .Select(team => new TeamEvidence(
                team.Id,
                team.Name,
                team.RegistrationStatus,
                team.IsBanned,
                team.DeletedAt))
            .ToDictionaryAsync(team => team.Id, ct);
        var challengeTitles = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challengeIds.Contains(challenge.Id))
            .Join(
                db.Challenges.AsNoTracking(),
                competitionChallenge => competitionChallenge.ChallengeId,
                challenge => challenge.Id,
                (competitionChallenge, challenge) => new
                {
                    competitionChallenge.Id,
                    Title = competitionChallenge.CustomTitle ?? challenge.Title
                })
            .ToDictionaryAsync(item => item.Id, item => item.Title, ct);

        var evidence = facts.Select(fact =>
        {
            var relevantFirstCorrects = firstCorrects
                .Where(first => first.CompetitionChallengeId == fact.CompetitionChallengeId)
                .ToArray();
            var ownFirstCorrect = fact.TeamId is Guid candidateTeamId
                ? relevantFirstCorrects.SingleOrDefault(first => first.TeamId == candidateTeamId)
                : null;
            var hasEarlierCorrect = ownFirstCorrect is not null && IsBefore(
                ownFirstCorrect.OccurredAt,
                ownFirstCorrect.Id,
                fact.OccurredAt,
                fact.Id);
            var earlierOtherTeams = relevantFirstCorrects
                .Where(first => first.TeamId != fact.TeamId
                    && IsBefore(first.OccurredAt, first.Id, fact.OccurredAt, fact.Id))
                .ToArray();
            var candidateIsEligible = fact.TeamId is Guid factTeamId
                && IsEligible(teams.GetValueOrDefault(factTeamId));
            var eligibilityRequiresReview = competition.Mode == GameMode.Ctf
                && (!candidateIsEligible
                    || earlierOtherTeams.Any(first => !IsEligible(
                        teams.GetValueOrDefault(first.TeamId))));
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
                fact.TeamId is Guid teamId
                    ? teams.GetValueOrDefault(teamId)?.Name ?? teamId.ToString()
                    : null,
                competition.Mode,
                fact.Kind,
                fact.Result,
                fact.FailureCode,
                fact.OccurredAt,
                hasEarlierCorrect,
                earlierOtherTeams.Count(first => IsEligible(teams.GetValueOrDefault(first.TeamId))),
                eligibilityRequiresReview,
                historicalResults,
                recordedBloodRanks);
        }).ToArray();
        await transaction.CommitAsync(ct);
        return new(HistoricalAdjudicationPreviewReadState.Available, evidence);
    }

    private static bool IsBefore(
        DateTimeOffset leftOccurredAt,
        Guid leftId,
        DateTimeOffset rightOccurredAt,
        Guid rightId) =>
        leftOccurredAt < rightOccurredAt
        || leftOccurredAt == rightOccurredAt
        && string.CompareOrdinal(leftId.ToString("N"), rightId.ToString("N")) < 0;

    private static bool IsEligible(TeamEvidence? team) =>
        team is
        {
            RegistrationStatus: NoCTF.Domain.Teams.TeamRegistrationStatus.Approved,
            IsBanned: false,
            DeletedAt: null
        };

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
        GameplayFactFailureCode? FailureCode,
        DateTimeOffset OccurredAt);

    private sealed record FirstCorrectFact(
        Guid Id,
        Guid CompetitionChallengeId,
        Guid TeamId,
        DateTimeOffset OccurredAt);

    private sealed record TeamEvidence(
        Guid Id,
        string Name,
        NoCTF.Domain.Teams.TeamRegistrationStatus RegistrationStatus,
        bool IsBanned,
        DateTimeOffset? DeletedAt);

    private sealed record EventGroup(
        Guid GameplayFactId,
        CompetitionEventKind Kind,
        string PayloadJson,
        int Count);
}
