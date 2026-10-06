using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.GameplayFacts.Management;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Infrastructure.GameplayFacts.Management;

public sealed class GameplayFactManagementStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox) : IGameplayFactManagementStore
{
    public async Task<GameplayFactListPage> ListAdminPageAsync(
        GameplayFactListFilter filter,
        int offset,
        int limit,
        bool desc,
        CancellationToken ct)
    {
        var query = db.GameplayFacts.AsNoTracking()
            .Where(submission => submission.CompetitionId == filter.CompetitionId);
        if (filter.CompetitionChallengeId is Guid challengeId)
            query = query.Where(submission => submission.CompetitionChallengeId == challengeId);
        if (filter.TeamId is Guid teamId)
            query = query.Where(submission => submission.TeamId == teamId);
        if (filter.VictimTeamId is Guid victimTeamId)
            query = query.Where(submission => submission.VictimTeamId == victimTeamId);
        if (filter.ActorUserId is Guid actorUserId)
            query = query.Where(submission => submission.ActorUserId == actorUserId);
        if (filter.Kind is GameplayFactKind kind)
            query = query.Where(submission => submission.Kind == kind);
        else
            query = query.Where(submission => submission.Kind != GameplayFactKind.AttachmentDownload);
        if (filter.State is GameplayFactState state)
            query = query.Where(submission => submission.State == state);
        if (filter.Result is GameplayFactResult result)
            query = query.Where(fact => fact.Result == result);
        if (filter.FailureCode is GameplayFactFailureCode failure)
            query = query.Where(fact => fact.FailureCode == failure);
        if (filter.OccurredFrom is DateTimeOffset from)
            query = query.Where(submission => submission.OccurredAt >= from);
        if (filter.OccurredTo is DateTimeOffset to)
            query = query.Where(submission => submission.OccurredAt < to);
        if (!string.IsNullOrEmpty(filter.Value))
            query = query.Where(submission => submission.Value == filter.Value);
        if (filter.ReferenceKind is GameplayFactReferenceKind referenceKind)
            query = query.Where(fact => fact.ReferenceKind == referenceKind);
        if (filter.ReferenceId is Guid referenceId)
            query = query.Where(fact => fact.ReferenceId == referenceId);

        var total = await query.CountAsync(ct);
        var ordered = desc
            ? query.OrderByDescending(submission => submission.OccurredAt)
                .ThenByDescending(submission => submission.Id)
            : query.OrderBy(submission => submission.OccurredAt)
                .ThenBy(submission => submission.Id);
        var items = await Project(ordered.Skip(offset).Take(limit)).ToListAsync(ct);
        return new GameplayFactListPage(items, total);
    }

    public async Task<GameplayFactListPage?> ListPlayerPageAsync(
        Guid competitionId,
        Guid userId,
        Guid? competitionChallengeId,
        GameplayFactKind? kind,
        int offset,
        int limit,
        bool desc,
        CancellationToken ct)
    {
        var teamId = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId && team.Members.Any(member => member.UserId == userId))
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(ct);
        if (teamId is null)
            return null;

        var submissions = db.GameplayFacts.AsNoTracking()
            .Where(submission =>
                submission.CompetitionId == competitionId
                && submission.TeamId == teamId.Value
                && (submission.Kind == GameplayFactKind.FlagAttempt
                    || submission.Kind == GameplayFactKind.BreakAttempt
                    || submission.Kind == GameplayFactKind.FixAttempt
                    || submission.Kind == GameplayFactKind.HintUnlock));
        if (competitionChallengeId is Guid challengeId)
            submissions = submissions.Where(submission =>
                submission.CompetitionChallengeId == challengeId);
        if (kind is GameplayFactKind factKind)
            submissions = submissions.Where(submission => submission.Kind == factKind);

        var total = await submissions.CountAsync(ct);
        var ordered = desc
            ? submissions.OrderByDescending(submission => submission.OccurredAt)
                .ThenByDescending(submission => submission.Id)
            : submissions.OrderBy(submission => submission.OccurredAt)
                .ThenBy(submission => submission.Id);
        var items = await Project(ordered.Skip(offset).Take(limit)).ToListAsync(ct);
        return new GameplayFactListPage(items.Select(item => item with
        {
            Result = GameplayFactResultDisclosure.PlayerResult(item.Result, item.FailureCode),
            FailureCode = GameplayFactResultDisclosure.PlayerFailureCode(item.FailureCode)
        }).ToArray(), total);
    }

    public async Task<IReadOnlyList<GameplayFactListItem>> ListAdminAsync(
        GameplayFactListFilter filter,
        DateTimeOffset? beforeOccurredAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct)
    {
        var query = db.GameplayFacts.AsNoTracking()
            .Where(submission => submission.CompetitionId == filter.CompetitionId);
        if (filter.CompetitionChallengeId is Guid challengeId)
            query = query.Where(submission =>
                submission.CompetitionChallengeId == challengeId);
        if (filter.TeamId is Guid teamId)
            query = query.Where(submission => submission.TeamId == teamId);
        if (filter.VictimTeamId is Guid victimTeamId)
            query = query.Where(submission => submission.VictimTeamId == victimTeamId);
        if (filter.ActorUserId is Guid actorUserId)
            query = query.Where(submission => submission.ActorUserId == actorUserId);
        if (filter.Kind is GameplayFactKind kind)
            query = query.Where(submission => submission.Kind == kind);
        else
            query = query.Where(submission => submission.Kind != GameplayFactKind.AttachmentDownload);
        if (filter.State is GameplayFactState state)
            query = query.Where(submission => submission.State == state);
        if (filter.Result is GameplayFactResult result)
            query = query.Where(fact => fact.Result == result);
        if (filter.FailureCode is GameplayFactFailureCode failure)
            query = query.Where(fact => fact.FailureCode == failure);
        if (filter.OccurredFrom is DateTimeOffset from)
            query = query.Where(submission => submission.OccurredAt >= from);
        if (filter.OccurredTo is DateTimeOffset to)
            query = query.Where(submission => submission.OccurredAt < to);
        if (!string.IsNullOrEmpty(filter.Value))
        {
            query = query.Where(submission => submission.Value == filter.Value);
        }
        if (filter.ReferenceKind is GameplayFactReferenceKind referenceKind)
            query = query.Where(fact => fact.ReferenceKind == referenceKind);
        if (filter.ReferenceId is Guid referenceId)
            query = query.Where(fact => fact.ReferenceId == referenceId);
        return await Project(Page(query, beforeOccurredAt, beforeId, limit))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GameplayFactListItem>?> ListPlayerAsync(
        Guid competitionId,
        Guid userId,
        Guid? competitionChallengeId,
        GameplayFactKind? kind,
        DateTimeOffset? beforeOccurredAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct)
    {
        var teamId = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId && team.Members.Any(member => member.UserId == userId))
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(ct);
        if (teamId is null)
            return null;
        var submissions = db.GameplayFacts.AsNoTracking()
            .Where(submission =>
                submission.CompetitionId == competitionId
                && submission.TeamId == teamId.Value
                && (submission.Kind == GameplayFactKind.FlagAttempt
                    || submission.Kind == GameplayFactKind.BreakAttempt
                    || submission.Kind == GameplayFactKind.FixAttempt
                    || submission.Kind == GameplayFactKind.HintUnlock));
        if (competitionChallengeId is Guid challengeId)
            submissions = submissions.Where(submission =>
                submission.CompetitionChallengeId == challengeId);
        if (kind is GameplayFactKind factKind)
            submissions = submissions.Where(submission => submission.Kind == factKind);
        var items = await Project(Page(
                submissions,
                beforeOccurredAt,
                beforeId,
                limit))
            .ToListAsync(ct);
        return items.Select(item => item with
        {
            Result = GameplayFactResultDisclosure.PlayerResult(item.Result, item.FailureCode),
            FailureCode = GameplayFactResultDisclosure.PlayerFailureCode(item.FailureCode)
        }).ToArray();
    }

    public Task<PlayerGameplayFactValue?> ReadPlayerValueAsync(
        Guid competitionId,
        Guid gameplayFactId,
        Guid userId,
        CancellationToken ct) =>
        db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.Id == gameplayFactId
                && fact.CompetitionId == competitionId
                && fact.TeamId != null
                && fact.Value != null
                && (fact.Kind == GameplayFactKind.FlagAttempt
                    || fact.Kind == GameplayFactKind.BreakAttempt))
            .Join(
                db.Teams.AsNoTracking()
                    .Where(team => team.CompetitionId == competitionId
                        && team.Members.Any(member => member.UserId == userId)),
                fact => fact.TeamId,
                team => (Guid?)team.Id,
                (fact, _) => new PlayerGameplayFactValue(
                    fact.Id,
                    fact.Kind,
                    fact.Value!))
            .SingleOrDefaultAsync(ct);

    public async Task QueueDrainAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset cutoff,
        bool rejudge,
        Guid? gameplayFactId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (rejudge)
            await outbox.PublishAsync(new DrainGameplayFactRejudge(
                competitionId, competitionChallengeId, cutoff, gameplayFactId));
        else
            await outbox.PublishAsync(new DrainGameplayFactEvaluation(
                competitionId, competitionChallengeId, cutoff, gameplayFactId));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
    }

    private static IQueryable<GameplayFact> Page(
        IQueryable<GameplayFact> query,
        DateTimeOffset? beforeOccurredAt,
        Guid? beforeId,
        int limit)
    {
        if (beforeOccurredAt is DateTimeOffset receivedAt && beforeId is Guid id)
            query = query.Where(submission =>
                submission.OccurredAt < receivedAt
                || submission.OccurredAt == receivedAt
                && submission.Id.CompareTo(id) < 0);
        return query
            .OrderByDescending(submission => submission.OccurredAt)
            .ThenByDescending(submission => submission.Id)
            .Take(limit);
    }

    private IQueryable<GameplayFactListItem> Project(IQueryable<GameplayFact> submissions) =>
        submissions.Select(submission => new GameplayFactListItem(
            submission.Id,
            submission.CompetitionId,
            submission.CompetitionChallengeId,
            submission.TeamId,
            submission.VictimTeamId,
            submission.ActorUserId,
            submission.Kind,
            submission.State,
            submission.Result,
            submission.FailureCode,
            submission.ReferenceKind,
            submission.ReferenceId,
            submission.Value,
            submission.OccurredAt,
            submission.UpdatedAt));
}
