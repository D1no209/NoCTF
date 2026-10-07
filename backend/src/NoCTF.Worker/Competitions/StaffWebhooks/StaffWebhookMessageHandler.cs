using NoCTF.Application.Competitions.StaffWebhooks;
using NoCTF.Application.Competitions.Webhooks;
using Wolverine;

namespace NoCTF.Worker.Competitions.StaffWebhooks;

public static class StaffWebhookMessageHandler
{
    public static async Task Handle(DeliverStaffWebhook command, IStaffWebhookStore store, ICompetitionWebhookSender sender, CancellationToken ct)
    {
        var prepared = await store.PrepareAsync(command, ct);
        if (prepared.Delivery is not { } delivery) return;
        try
        {
            var outcome = await sender.SendAsync(delivery, ct);
            await store.CompleteAsync(command, outcome.HttpStatusCode, outcome.Result == CompetitionWebhookSendResult.Delivered,
                outcome.Result == CompetitionWebhookSendResult.ReceiverGone, null, ct);
        }
        catch (CompetitionWebhookTransientException exception)
        { await store.CompleteAsync(command, exception.HttpStatusCode, false, false, exception.RetryAfter, ct); }
        catch (CompetitionWebhookPermanentException exception)
        { await store.CompleteAsync(command, exception.HttpStatusCode, false, true, null, ct); }
    }
}
