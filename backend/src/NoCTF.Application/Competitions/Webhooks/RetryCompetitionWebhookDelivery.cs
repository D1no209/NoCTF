namespace NoCTF.Application.Competitions.Webhooks;

public enum CompetitionWebhookRetryFailure : short
{
    NotFound,
    TargetUnavailable,
    NotRetryable
}

public sealed class RetryCompetitionWebhookDelivery(ICompetitionWebhookDeliveryStore store, TimeProvider clock)
{
    public Task<CompetitionWebhookRetryFailure?> ExecuteAsync(
        Guid competitionId, Guid targetId, Guid eventId, CancellationToken ct) =>
        store.RetryProjectionFailureAsync(competitionId, targetId, eventId, clock.GetUtcNow(), ct);
}
