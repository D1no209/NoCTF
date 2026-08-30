using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker;

internal static partial class BackendMessageOperations
{
    public static Task DrainGameplayFactEvaluationAsync(
        DrainGameplayFactEvaluation message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        DrainGameplayFactsAsync(
            message.CompetitionId,
            message.CompetitionChallengeId,
            message.Cutoff,
            rejudge: false,
            message.GameplayFactId,
            message,
            db,
            outbox,
            timeProvider,
            cancellationToken);

    public static Task DrainGameplayFactRejudgeAsync(
        DrainGameplayFactRejudge message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        DrainGameplayFactsAsync(
            message.CompetitionId,
            message.CompetitionChallengeId,
            message.Cutoff,
            rejudge: true,
            message.GameplayFactId,
            message,
            db,
            outbox,
            timeProvider,
            cancellationToken);

    private static async Task DrainGameplayFactsAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset cutoff,
        bool rejudge,
        Guid? requestedGameplayFactId,
        object continuationMessage,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        const int batchSize = 500;
        var candidates = db.GameplayFacts.Where(submission =>
            submission.CompetitionId == competitionId
            && submission.CompetitionChallengeId == competitionChallengeId
            && submission.OccurredAt <= cutoff);
        if (requestedGameplayFactId is Guid gameplayFactId)
        {
            candidates = candidates.Where(submission =>
                submission.Id == gameplayFactId
                && submission.State != NoCTF.Domain.Gameplay.GameplayFactState.Queued
                && submission.State != NoCTF.Domain.Gameplay.GameplayFactState.Processing);
        }
        else if (rejudge)
        {
            candidates = candidates.Where(submission =>
                submission.Result != null
                && (submission.Kind == NoCTF.Domain.Gameplay.GameplayFactKind.FlagAttempt
                    || submission.Kind == NoCTF.Domain.Gameplay.GameplayFactKind.BreakAttempt)
                && submission.State != NoCTF.Domain.Gameplay.GameplayFactState.Queued
                && submission.State != NoCTF.Domain.Gameplay.GameplayFactState.Processing);
        }
        else
        {
            candidates = candidates.Where(submission =>
                submission.State == NoCTF.Domain.Gameplay.GameplayFactState.Pending
                || (submission.State == NoCTF.Domain.Gameplay.GameplayFactState.PlatformFailed
                    && submission.Result == null));
        }

        var candidateIds = await candidates.AsNoTracking()
            .OrderBy(submission => submission.OccurredAt)
            .ThenBy(submission => submission.Id)
            .Take(requestedGameplayFactId is null ? batchSize : 1)
            .Select(submission => submission.Id)
            .ToArrayAsync(cancellationToken);
        if (candidateIds.Length == 0)
            return;

        var now = timeProvider.GetUtcNow();
        await candidates.Where(submission => candidateIds.Contains(submission.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    submission => submission.State,
                    NoCTF.Domain.Gameplay.GameplayFactState.Queued)
                .SetProperty(submission => submission.UpdatedAt, now),
                cancellationToken);
        var submissions = await db.GameplayFacts.AsNoTracking()
            .Where(submission => candidateIds.Contains(submission.Id))
            .OrderBy(submission => submission.OccurredAt)
            .ThenBy(submission => submission.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var submission in submissions)
            await outbox.PublishAsync(new EvaluateGameplayFact(submission.Id));
        if (candidateIds.Length == batchSize && requestedGameplayFactId is null)
            await outbox.PublishAsync(continuationMessage);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }
}
