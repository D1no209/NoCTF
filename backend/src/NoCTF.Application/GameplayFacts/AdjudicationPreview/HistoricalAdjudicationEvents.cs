namespace NoCTF.Application.GameplayFacts.AdjudicationPreview;

public sealed record HistoricalAdjudicationEventPage(IReadOnlyList<AdjudicationEventEvidence> Events,
    DateTimeOffset? NextBeforeOccurredAt, Guid? NextBeforeId);

public interface IHistoricalAdjudicationEventStore
{
    Task<HistoricalAdjudicationEventPage?> ReadEventsAsync(Guid competitionId, Guid gameplayFactId,
        DateTimeOffset? beforeOccurredAt, Guid? beforeId, int limit, bool includeInternalTeams, CancellationToken ct);
}

public sealed class ReadHistoricalAdjudicationEvents(IHistoricalAdjudicationEventStore store)
{
    public Task<HistoricalAdjudicationEventPage?> ExecuteAsync(Guid competitionId, Guid gameplayFactId,
        DateTimeOffset? beforeOccurredAt, Guid? beforeId, int limit, bool includeInternalTeams, CancellationToken ct) =>
        store.ReadEventsAsync(competitionId, gameplayFactId, beforeOccurredAt, beforeId, limit, includeInternalTeams, ct);
}
