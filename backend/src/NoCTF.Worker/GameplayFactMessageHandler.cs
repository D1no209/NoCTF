using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Observability;

namespace NoCTF.Worker;

public sealed class GameplayFactMessageHandler(
    IGameplayFactProcessor processor,
    NoCtfDbContext db,
    IGameplayFactStateChangedNotification notifications)
{
    public async Task Handle(
        EvaluateGameplayFact message,
        CancellationToken cancellationToken)
    {
        NoCtfTelemetry.RecordGameplayFactDelivery(GameplayFactDeliveryOutcome.Received);
        await processor.ProcessAsync(message.GameplayFactId, message.ExpectedConcurrencyStamp, cancellationToken);
    }

    public async Task Handle(
        GameplayFactStateChanged message,
        CancellationToken cancellationToken)
    {
        var fact = await db.GameplayFacts.AsNoTracking()
            .Where(candidate => candidate.Id == message.GameplayFactId)
            .Select(candidate => new { candidate.Id, candidate.ActorUserId, candidate.CompetitionId,
                candidate.TeamId, candidate.CompetitionChallengeId, candidate.Kind, candidate.State,
                candidate.Result, candidate.FailureCode, candidate.ConcurrencyStamp,
                candidate.OccurredAt, candidate.UpdatedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (fact?.ActorUserId is not Guid userId || fact.State != message.State || fact.CompetitionChallengeId is null
            || message.ConcurrencyStamp != Guid.Empty && fact.ConcurrencyStamp != message.ConcurrencyStamp)
            return;
        var view = new GameplayFactStatusView(
            fact.Id,
            fact.CompetitionId,
            fact.TeamId,
            fact.CompetitionChallengeId.Value,
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
