using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Lifecycle;

internal static class CompetitionEffectiveRuntimeReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<CompetitionEffectiveRuntime> ReadAsync(
        NoCtfDbContext db,
        Guid competitionId,
        DateTimeOffset competitionStartAt,
        DateTimeOffset competitionEndAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var stored = await db.CompetitionEvents.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId
                && item.Kind == CompetitionEventKind.CompetitionLifecycleChanged)
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.Id)
            .Select(item => new { item.Id, item.OccurredAt, item.PayloadJson })
            .ToArrayAsync(cancellationToken);
        var transitions = stored.Select(item =>
        {
            var payload = JsonSerializer.Deserialize<LifecyclePayload>(item.PayloadJson, JsonOptions)
                ?? throw new InvalidOperationException(
                    $"Competition lifecycle event {item.Id} has an invalid payload.");
            return new CompetitionLifecycleMoment(item.Id, item.OccurredAt, payload.From, payload.To);
        });
        return CompetitionEffectiveRuntimePolicy.Calculate(
            competitionStartAt,
            competitionEndAt,
            now,
            transitions);
    }

    private sealed record LifecyclePayload(
        int SchemaVersion,
        CompetitionStatus From,
        CompetitionStatus To);
}
