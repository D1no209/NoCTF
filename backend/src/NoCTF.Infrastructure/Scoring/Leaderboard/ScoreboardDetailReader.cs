using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Lifecycle;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class ScoreboardDetailReader(NoCtfDbContext db) : IScoreboardDetailReader
{
    public async Task<IReadOnlyList<ScoreboardSlotDetailFact>> ReadSlotAsync(
        ScoreboardSlotDetailQuery query,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        CompetitionOfficialWindow? officialWindow = null;
        if (query.Mode == GameMode.Ctf)
        {
            var competition = await db.Competitions.AsNoTracking()
                .Where(candidate => candidate.Id == query.CompetitionId)
                .Select(candidate => new { candidate.StartAt, candidate.EndAt })
                .SingleAsync(cancellationToken);
            officialWindow = await CompetitionOfficialWindowReader.ReadAsync(
                db,
                query.CompetitionId,
                competition.StartAt,
                competition.EndAt,
                cancellationToken);
        }
        var ctfWindow = officialWindow.GetValueOrDefault();
        var facts = db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == query.CompetitionId
                && fact.TeamId == query.TeamId
                && fact.CompetitionChallengeId == query.CompetitionChallengeId
                && fact.OccurredAt <= query.DataAsOf);

        facts = query.Mode switch
        {
            GameMode.Ctf => facts.Where(fact => (fact.Kind == GameplayFactKind.FlagAttempt
                    || fact.Kind == GameplayFactKind.HintUnlock)
                && fact.OccurredAt >= ctfWindow.StartAt
                && fact.OccurredAt < ctfWindow.EndAt),
            GameMode.Awd => facts.Where(fact => fact.Kind == GameplayFactKind.FlagAttempt
                && fact.ReferenceKind == GameplayFactReferenceKind.AwdRound
                && fact.ReferenceId == query.RoundId),
            GameMode.Awdp => facts.Where(fact => (fact.Kind == GameplayFactKind.BreakAttempt
                    || fact.Kind == GameplayFactKind.FixAttempt)
                && fact.OccurredAt >= query.RoundStartAt
                && fact.OccurredAt < query.RoundEndAt),
            GameMode.Koh => facts.Where(fact => fact.Kind == GameplayFactKind.KohControlObservation),
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.Mode, "Unsupported game mode.")
        };

        if (query.BeforeOccurredAt is DateTimeOffset beforeAt && query.BeforeId is Guid beforeId)
        {
            facts = facts.Where(fact => fact.OccurredAt < beforeAt
                || fact.OccurredAt == beforeAt && fact.Id.CompareTo(beforeId) < 0);
        }

        var rows = await facts
            .OrderByDescending(fact => fact.OccurredAt)
            .ThenByDescending(fact => fact.Id)
            .Select(fact => new
            {
                fact.Id,
                fact.Kind,
                fact.State,
                fact.Result,
                fact.FailureCode,
                fact.ReferenceKind,
                fact.ReferenceId,
                fact.ActorUserId,
                fact.VictimTeamId,
                fact.OccurredAt,
                fact.UpdatedAt
            })
            .Take(query.Limit)
            .ToArrayAsync(cancellationToken);
        var factIds = rows.Select(row => row.Id).ToArray();
        var latestEventIds = db.CompetitionEvents.AsNoTracking()
            .Where(@event => @event.CompetitionId == query.CompetitionId
                && @event.Kind == CompetitionEventKind.GameplayFactAdjudicated
                && @event.SubjectType == EntityReferenceKind.GameplayFact
                && factIds.Contains(@event.SubjectId)
                && @event.OccurredAt <= query.DataAsOf)
            .GroupBy(@event => @event.SubjectId)
            .Select(group => group
                .OrderByDescending(@event => @event.OccurredAt)
                .ThenByDescending(@event => @event.Id)
                .Select(@event => @event.Id)
                .First());
        var adjudications = await db.CompetitionEvents.AsNoTracking()
            .Where(@event => latestEventIds.Contains(@event.Id))
            .ToDictionaryAsync(@event => @event.SubjectId, cancellationToken);
        var actorIds = rows
            .Where(row => row.ActorUserId is not null)
            .Select(row => row.ActorUserId!.Value)
            .Distinct()
            .ToArray();
        var actorNames = await db.Users.AsNoTracking()
            .Where(user => actorIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName, cancellationToken);

        var result = rows.Select(row =>
        {
            adjudications.TryGetValue(row.Id, out var adjudication);
            var useCurrent = row.UpdatedAt <= query.DataAsOf;
            var state = adjudication?.GameplayFactState
                ?? (useCurrent ? row.State : GameplayFactState.Queued);
            var factResult = adjudication is not null
                ? adjudication.GameplayFactResult
                : useCurrent ? row.Result : null;
            return new ScoreboardSlotDetailFact(
                row.Id,
                row.Kind,
                state,
                factResult,
                useCurrent ? row.FailureCode : null,
                row.ReferenceKind,
                row.ReferenceId,
                row.ActorUserId,
                row.ActorUserId is Guid actorUserId
                    ? actorNames.GetValueOrDefault(actorUserId)
                    : null,
                useCurrent ? row.VictimTeamId : null,
                useCurrent,
                row.OccurredAt);
        }).ToArray();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<ScoreboardAdjustmentDetailFact>> ReadAdjustmentsAsync(
        ScoreboardAdjustmentDetailQuery query,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var facts = db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == query.CompetitionId
                && fact.TeamId == query.TeamId
                && fact.Kind == GameplayFactKind.ManualAdjustment
                && fact.Result == GameplayFactResult.Applied
                && fact.OccurredAt <= query.DataAsOf);
        if (query.BeforeOccurredAt is DateTimeOffset beforeAt && query.BeforeId is Guid beforeId)
        {
            facts = facts.Where(fact => fact.OccurredAt < beforeAt
                || fact.OccurredAt == beforeAt && fact.Id.CompareTo(beforeId) < 0);
        }

        var rows = await facts
            .OrderByDescending(fact => fact.OccurredAt)
            .ThenByDescending(fact => fact.Id)
            .Select(fact => new { fact.Id, fact.ActorUserId, fact.OccurredAt, fact.Value })
            .Take(query.Limit)
            .ToArrayAsync(cancellationToken);
        var actorIds = rows
            .Where(row => row.ActorUserId is not null)
            .Select(row => row.ActorUserId!.Value)
            .Distinct()
            .ToArray();
        var actorNames = await db.Users.AsNoTracking()
            .Where(user => actorIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName, cancellationToken);
        var result = rows.Select(row => new ScoreboardAdjustmentDetailFact(
            row.Id,
            row.ActorUserId,
            row.ActorUserId is Guid actorUserId
                ? actorNames.GetValueOrDefault(actorUserId)
                : null,
            row.OccurredAt,
            long.Parse(row.Value!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)))
            .ToArray();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
