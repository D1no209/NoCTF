using Microsoft.EntityFrameworkCore;

namespace NoCTF.Infrastructure.Competitions.Webhooks;

/// <summary>Immutable public scoreboard generation captured for one freeze boundary.</summary>
[PrimaryKey(nameof(CompetitionId), nameof(FrozenAt))]
public sealed class CompetitionWebhookFrozenProjection
{
    public Guid CompetitionId { get; set; }
    public DateTimeOffset FrozenAt { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public long SourceEventSequenceThrough { get; set; }
    public byte[] Payload { get; set; } = [];
}
