using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

/// <summary>Runner delivery uses the same lifecycle and round clock as issuance.</summary>
public static class AwdFlagDeliveryWindow
{
    public static async Task<DateTimeOffset> EndsAtAsync(NoCtfDbContext db, Competition competition,
        DateTimeOffset now, CancellationToken ct)
    {
        var deadline = await AwdCompetitionDeadline.ReadAsync(db, competition.Id, competition.StartAt,
            competition.EndAt, (AwdCompetitionModeConfiguration)competition.ModeConfiguration!, now, ct);
        return deadline.EndsAt;
    }
}
