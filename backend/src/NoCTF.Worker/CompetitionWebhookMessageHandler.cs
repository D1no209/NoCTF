using NoCTF.Application.Competitions.Webhooks;
using Wolverine;

namespace NoCTF.Worker;

public sealed class CompetitionWebhookMessageHandler(
    ICompetitionWebhookDeliveryStore deliveries,
    ICompetitionWebhookSender sender,
    ICompetitionWebhookTestStatusStore testStatuses,
    IMessageBus bus,
    TimeProvider timeProvider)
{
    private const int DispatchBatchSize = 100;

    public async Task Handle(
        DispatchCompetitionWebhooks message,
        CancellationToken cancellationToken)
    {
        var batch = await deliveries.PrepareBatchAsync(
            message,
            DispatchBatchSize,
            cancellationToken);
        foreach (var delivery in batch.Deliveries)
            await bus.PublishAsync(delivery);
        if (batch.NextAfterTargetId is Guid cursor)
        {
            await bus.PublishAsync(message with { AfterTargetId = cursor });
        }
    }

    public async Task Handle(
        DeliverCompetitionWebhook message,
        CancellationToken cancellationToken)
    {
        var delivery = await deliveries.PrepareDeliveryAsync(message, cancellationToken);
        if (delivery.State != CompetitionWebhookDeliveryReadState.Ready)
            return;
        var result = await sender.SendAsync(delivery, cancellationToken);
        if (result == CompetitionWebhookSendResult.ReceiverGone)
        {
            await deliveries.DisableGoneAsync(
                delivery.CompetitionId,
                delivery.TargetId,
                delivery.Endpoint!,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
    }

    public async Task Handle(
        TestCompetitionWebhook message,
        CancellationToken cancellationToken)
    {
        var delivery = await deliveries.PrepareTestDeliveryAsync(message, cancellationToken);
        if (delivery.State != CompetitionWebhookDeliveryReadState.Ready)
        {
            await testStatuses.CompleteAsync(
                message.DeliveryId,
                CompetitionWebhookTestState.Failed,
                timeProvider.GetUtcNow(),
                CompetitionWebhookTestFailureCode.TargetUnavailable,
                cancellationToken);
            return;
        }
        try
        {
            var result = await sender.SendAsync(delivery, cancellationToken);
            if (result == CompetitionWebhookSendResult.ReceiverGone)
            {
                await deliveries.DisableGoneAsync(
                    delivery.CompetitionId,
                    delivery.TargetId,
                    delivery.Endpoint!,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                await testStatuses.CompleteAsync(
                    message.DeliveryId,
                    CompetitionWebhookTestState.Failed,
                    timeProvider.GetUtcNow(),
                    CompetitionWebhookTestFailureCode.ReceiverGone,
                    cancellationToken);
                return;
            }
            await testStatuses.CompleteAsync(
                message.DeliveryId,
                CompetitionWebhookTestState.Succeeded,
                timeProvider.GetUtcNow(),
                null,
                cancellationToken);
        }
        catch (CompetitionWebhookPermanentException)
        {
            await testStatuses.CompleteAsync(
                message.DeliveryId,
                CompetitionWebhookTestState.Failed,
                timeProvider.GetUtcNow(),
                CompetitionWebhookTestFailureCode.PermanentFailure,
                cancellationToken);
            throw;
        }
    }
}
