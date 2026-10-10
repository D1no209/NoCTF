using System.Security.Cryptography;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Rounds;

public static class LiveSoloCompetitionPausePolicy
{
    public static IReadOnlyList<LiveSoloPauseInterval> Project(Guid roundId, DateTimeOffset createdAt, DateTimeOffset now,
        IEnumerable<CompetitionLifecycleMoment> moments)
    {
        var result = new List<LiveSoloPauseInterval>();
        CompetitionLifecycleMoment? opening = null;
        foreach (var moment in moments.Where(x => x.OccurredAt <= now).OrderBy(x => x.OccurredAt).ThenBy(x => x.EventId))
        {
            if (moment.To == CompetitionStatus.Paused && opening is null) opening = moment;
            if (moment.To == CompetitionStatus.Paused || opening is null) continue;
            if (moment.OccurredAt > createdAt) result.Add(Interval(roundId, opening, createdAt, moment.OccurredAt));
            opening = null;
        }
        if (opening is not null) result.Add(Interval(roundId, opening, createdAt, null));
        return result;
    }
    private static LiveSoloPauseInterval Interval(Guid roundId, CompetitionLifecycleMoment opening, DateTimeOffset createdAt, DateTimeOffset? end)
    {
        Span<byte> identity = stackalloc byte[32]; roundId.TryWriteBytes(identity[..16]); opening.EventId.TryWriteBytes(identity[16..]);
        return new() { Id = new Guid(SHA256.HashData(identity).AsSpan(0, 16)), RoundId = roundId, Source = LiveSoloPauseSource.Competition,
            StartedAt = opening.OccurredAt < createdAt ? createdAt : opening.OccurredAt, EndedAt = end };
    }
}
