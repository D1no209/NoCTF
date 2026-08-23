using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Application.Competitions.Events;

namespace NoCTF.Infrastructure.Challenges.Hints;

public sealed class ChallengeHintStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    TeamChallengeCriticalSection criticalSection,
    ICompetitionEventRecorder? eventRecorder = null) : IChallengeHintStore
{
    public ChallengeHintStore(
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        ICompetitionEventRecorder? eventRecorder = null)
        : this(
            db,
            outbox,
            new TeamChallengeCriticalSection(new AsyncKeyedLock.AsyncKeyedLocker<string>()),
            eventRecorder)
    { }

    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<IReadOnlyList<ChallengeHintView>?> ListAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        bool includeDeleted,
        CancellationToken ct)
    {
        if (!await ScopeExistsAsync(competitionId, competitionChallengeId, ct))
            return null;
        var entity = await db.CompetitionChallenges
            .SingleAsync(challenge => challenge.Id == competitionChallengeId, ct);
        return entity.Hints.Where(hint => includeDeleted || hint.HiddenAt == null)
            .Select(hint => Map(hint, entity.Id, entity.UpdatedAt)).ToArray();
    }

    public async Task<ChallengeHintView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        bool includeDeleted,
        CancellationToken ct)
    {
        if (!await ScopeExistsAsync(competitionId, competitionChallengeId, ct))
            return null;
        var entity = await db.CompetitionChallenges
            .SingleAsync(challenge => challenge.Id == competitionChallengeId, ct);
        var hint = entity.Hints.SingleOrDefault(item =>
            item.Id == hintId && (includeDeleted || item.HiddenAt == null));
        return hint is null ? null : Map(hint, entity.Id, entity.UpdatedAt);
    }

    public async Task<ChallengeHintSaveResult> SaveAsync(
        SaveChallengeHintCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await CompetitionStateReader.ReadAsync(db, command.CompetitionId, ct) is null)
            return new(null, ChallengeHintSaveFailure.ScopeNotFound);

        var challenge = await db.CompetitionChallenges
            .SingleOrDefaultAsync(item =>
                item.Id == command.CompetitionChallengeId &&
                item.CompetitionId == command.CompetitionId, ct);
        if (challenge is null)
            return new(null, ChallengeHintSaveFailure.ScopeNotFound);
        CompetitionChallengeHint hint;
        if (!command.IsCreate && command.HintId is Guid hintId)
        {
            hint = challenge.Hints.SingleOrDefault(item => item.Id == hintId && item.HiddenAt == null)
                ?? null!;
            if (hint is null)
                return new(null, ChallengeHintSaveFailure.HintNotFound);
        }
        else
        {
            var requestedId = command.HintId ?? Guid.CreateVersion7(command.Now);
            if (challenge.Hints.Any(item => item.Id == requestedId))
                return new(null, ChallengeHintSaveFailure.ResourceIdConflict);
            hint = new CompetitionChallengeHint
            {
                Id = requestedId
            };
            challenge.Hints.Add(hint);
        }
        var wasPublished = hint.PublishedAt is { } previousPublishedAt
            && previousPublishedAt <= command.Now;
        hint.Content = command.Content;
        hint.Cost = command.Cost;
        hint.PublishedAt = command.PublishedAt;
        challenge.Revision = checked(challenge.Revision + 1);
        challenge.UpdatedAt = command.Now;
        try
        {
            await db.SaveChangesAsync(ct);
            await LeaderboardDirty.MarkAsync(db, command.CompetitionId, ct);
            await QueueHintPublicationAsync(
                command.CompetitionId,
                challenge,
                hint,
                command.Now,
                wasPublished,
                ct);
            await transaction.CommitAsync(ct);
            await outbox.FlushOutgoingMessagesAsync();
            return new(Map(hint, challenge.Id, challenge.UpdatedAt));
        }
        catch (DbUpdateException) when (command.IsCreate)
        {
            return new(null, ChallengeHintSaveFailure.ResourceIdConflict);
        }
    }

    public async Task<bool> DeleteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await CompetitionStateReader.ReadAsync(db, competitionId, ct) is null)
            return false;

        var challenge = await db.CompetitionChallenges
            .SingleOrDefaultAsync(item =>
                item.Id == competitionChallengeId &&
                item.CompetitionId == competitionId, ct);
        var hint = challenge?.Hints.SingleOrDefault(item => item.Id == hintId && item.HiddenAt == null);
        if (hint is null)
            return false;
        hint.HiddenAt = now;
        challenge!.Revision = checked(challenge.Revision + 1);
        challenge.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await LeaderboardDirty.MarkAsync(db, competitionId, ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await CompetitionStateReader.ReadAsync(db, competitionId, ct) is null)
            return false;

        var challenge = await db.CompetitionChallenges
            .SingleOrDefaultAsync(item =>
                item.Id == competitionChallengeId &&
                item.CompetitionId == competitionId, ct);
        var hint = challenge?.Hints.SingleOrDefault(item =>
            item.Id == hintId && item.HiddenAt != null);
        if (hint is null)
            return false;
        hint.HiddenAt = null;
        challenge!.Revision = checked(challenge.Revision + 1);
        challenge.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await LeaderboardDirty.MarkAsync(db, competitionId, ct);
        await QueueHintPublicationAsync(
            competitionId,
            challenge,
            hint,
            now,
            wasPublished: false,
            ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return true;
    }

    public async Task<HintUnlockAttempt> UnlockAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid hintId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var competitionIsRunning = await db.Competitions.AsNoTracking().AnyAsync(
            competition => competition.Id == competitionId
                && competition.Status == CompetitionStatus.Running,
            ct);
        if (!competitionIsRunning)
            return HintUnlockAttempt.Failed(HintUnlockFailure.NotFound);
        var teamId = await db.Teams.AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId &&
                team.MemberIds.Contains(userId) &&
                team.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !team.IsBanned)
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(ct);
        if (teamId is null)
            return HintUnlockAttempt.Failed(HintUnlockFailure.NotFound);
        using var unlockLease = await criticalSection.AcquireAsync(
            db,
            teamId.Value,
            competitionChallengeId,
            ct);
        var challenge = await db.CompetitionChallenges
            .SingleOrDefaultAsync(item =>
                item.Id == competitionChallengeId &&
                item.CompetitionId == competitionId &&
                item.IsPublished, ct);
        var hint = challenge?.Hints.SingleOrDefault(item =>
            item.Id == hintId &&
            item.HiddenAt == null &&
            item.PublishedAt != null &&
            item.PublishedAt <= now);
        if (hint is null)
            return HintUnlockAttempt.Failed(HintUnlockFailure.NotFound);
        var existing = await db.GameplayFacts.AsNoTracking().Where(item =>
            item.CompetitionId == competitionId &&
            item.TeamId == teamId &&
            item.Kind == GameplayFactKind.HintUnlock &&
            item.ReferenceKind == GameplayFactReferenceKind.Hint &&
            item.ReferenceId == hintId)
            .OrderByDescending(item => item.OccurredAt)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is Guid existingId)
            return HintUnlockAttempt.Success(new(existingId, false));

        var gameplayFactId = Guid.CreateVersion7(now);
        db.GameplayFacts.Add(new GameplayFact
        {
            Id = gameplayFactId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId.Value,
            ActorUserId = userId,
            Kind = GameplayFactKind.HintUnlock,
            ReferenceKind = GameplayFactReferenceKind.Hint,
            ReferenceId = hintId,
            OccurredAt = now,
            State = GameplayFactState.Queued,
            UpdatedAt = now
        });
        await outbox.PublishAsync(new EvaluateGameplayFact(gameplayFactId));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return HintUnlockAttempt.Success(new(gameplayFactId, true));
    }

    private async Task QueueHintPublicationAsync(
        Guid competitionId,
        CompetitionChallenge challenge,
        CompetitionChallengeHint hint,
        DateTimeOffset now,
        bool wasPublished,
        CancellationToken ct)
    {
        if (hint.PublishedAt is not { } publishedAt)
            return;
        if (publishedAt <= now && wasPublished)
            return;

        var challengeTitle = await db.Challenges.AsNoTracking()
            .Where(template => template.Id == challenge.ChallengeId)
            .Select(template => challenge.CustomTitle ?? template.Title)
            .SingleAsync(ct);
        var message = new PublishHintNotification(
            competitionId,
            challenge.Id,
            hint.Id,
            challengeTitle,
            hint.Cost,
            publishedAt,
            challenge.Revision);
        if (publishedAt <= now)
            await outbox.PublishAsync(message);
        else
            await outbox.ScheduleAsync(message, publishedAt);
    }

    private Task<bool> ScopeExistsAsync(Guid competitionId, Guid competitionChallengeId, CancellationToken ct) =>
        db.CompetitionChallenges.AnyAsync(
            item => item.Id == competitionChallengeId && item.CompetitionId == competitionId, ct);

    private static ChallengeHintView Map(
        CompetitionChallengeHint hint,
        Guid competitionChallengeId,
        DateTimeOffset updatedAt) =>
        new(
            hint.Id, competitionChallengeId, hint.Content, hint.Cost,
            hint.PublishedAt, hint.HiddenAt, updatedAt, updatedAt);
}
