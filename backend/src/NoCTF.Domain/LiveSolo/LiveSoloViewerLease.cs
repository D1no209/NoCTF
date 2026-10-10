using NoCTF.Domain.Shared;

namespace NoCTF.Domain.LiveSolo;

public sealed class LiveSoloViewerLease : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid? UserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public DateTimeOffset ExpiresAt { get; set; }
}
