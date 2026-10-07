using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker;

public sealed class GameplayFactMessageHandler(
    IGameplayFactProcessor processor,
    NoCtfDbContext db,
    IGameplayFactStateChangedNotification notifications,
    IPostCommitMessagePublisher publisher)
{
    public async Task Handle(
        EvaluateGameplayFact message,
        CancellationToken cancellationToken)
    {
        var unlock = await db.GameplayFacts.AsNoTracking().Where(x => x.Id == message.GameplayFactId
            && x.Kind == NoCTF.Domain.Gameplay.GameplayFactKind.WriteUpUnlock
            && x.State == NoCTF.Domain.Gameplay.GameplayFactState.Queued)
            .Select(x => (Guid?)x.CompetitionId).SingleOrDefaultAsync(cancellationToken);
        if (unlock is Guid competitionId)
        {
            await publisher.PublishAsync(new ProjectLeaderboard(competitionId));
            await publisher.FlushCommittedMessagesAsync();
            return;
        }
        await processor.ProcessAsync(message.GameplayFactId, cancellationToken);
    }

    public async Task Handle(
        GameplayFactStateChanged message,
        CancellationToken cancellationToken)
    {
        var fact = await db.GameplayFacts.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == message.GameplayFactId,
                cancellationToken);
        if (fact?.ActorUserId is not Guid userId || fact.State != message.State)
            return;
        var view = new GameplayFactStatusView(
            fact.Id,
            fact.CompetitionId,
            fact.TeamId,
            fact.CompetitionChallengeId,
            fact.Kind,
            fact.State,
            GameplayFactResultDisclosure.PlayerResult(fact.Result, fact.FailureCode),
            GameplayFactResultDisclosure.PlayerFailureCode(fact.FailureCode),
            fact.OccurredAt,
            fact.UpdatedAt);
        await notifications.PublishAsync(
            new GameplayFactStateChangedNotification(userId, view),
            cancellationToken);
    }
}
