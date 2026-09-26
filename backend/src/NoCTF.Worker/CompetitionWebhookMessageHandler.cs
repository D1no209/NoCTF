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
        else
        {
            await deliveries.MarkDispatchCompletedAsync(
                message.EventId, timeProvider.GetUtcNow(), cancellationToken);
        }
    }

    public async Task Handle(
        DeliverCompetitionWebhook message,
        CancellationToken cancellationToken)
    {
        CompetitionWebhookDelivery delivery;
        try
        {
            delivery = await deliveries.PrepareDeliveryAsync(message, cancellationToken);
        }
        catch (CompetitionWebhookProjectionNotReadyException exception)
        {
            await deliveries.RecordProjectionWaitAsync(message, exception.Failure,
                timeProvider.GetUtcNow(), cancellationToken);
            return;
        }
        if (delivery.State != CompetitionWebhookDeliveryReadState.Ready)
            return;
        await deliveries.RecordHttpStartedAsync(
            message, timeProvider.GetUtcNow(), cancellationToken);
        try
        {
            var outcome = await sender.SendAsync(delivery, cancellationToken);
            await deliveries.RecordHttpCompletedAsync(
                message, outcome, timeProvider.GetUtcNow(), cancellationToken);
            if (outcome.Result == CompetitionWebhookSendResult.ReceiverGone)
                await deliveries.DisableGoneAsync(
                    delivery.CompetitionId, delivery.TargetId, delivery.Endpoint!,
                    timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (CompetitionWebhookTransientException exception)
        {
            await deliveries.RecordHttpFailureAsync(message,
                exception.HttpStatusCode, exception.RetryAfter, permanent: false,
                timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (CompetitionWebhookPermanentException exception)
        {
            await deliveries.RecordHttpFailureAsync(message,
                exception.HttpStatusCode, null, permanent: true,
                timeProvider.GetUtcNow(), cancellationToken);
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
            if (result.Result == CompetitionWebhookSendResult.ReceiverGone)
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
