using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Lifecycle;

internal static class CompetitionOfficialWindowReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<CompetitionOfficialWindow> ReadAsync(
        NoCtfDbContext db,
        Guid competitionId,
        DateTimeOffset startAt,
        DateTimeOffset scheduledEndAt,
        CancellationToken ct)
    {
        var lifecycleEvents = await db.CompetitionEvents.AsNoTracking()
            .Where(@event => @event.CompetitionId == competitionId
                && @event.Kind == CompetitionEventKind.CompetitionLifecycleChanged)
            .OrderBy(@event => @event.OccurredAt)
            .ThenBy(@event => @event.Id)
            .Select(@event => new { @event.OccurredAt, @event.PayloadJson })
            .ToArrayAsync(ct);
        var finishedAt = lifecycleEvents
            .Where(@event => IsFinished(@event.PayloadJson))
            .Select(@event => (DateTimeOffset?)@event.OccurredAt)
            .FirstOrDefault();
        return CompetitionOfficialWindow.Resolve(startAt, scheduledEndAt, finishedAt);
    }

    private static bool IsFinished(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<LifecyclePayload>(payloadJson, JsonOptions)?.To
                == CompetitionStatus.Finished;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record LifecyclePayload(CompetitionStatus To);
}
