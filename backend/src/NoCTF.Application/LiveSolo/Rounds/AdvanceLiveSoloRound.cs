namespace NoCTF.Application.LiveSolo.Rounds;

/// <summary>A durable wakeup, never a client clock or persisted next-run protocol.</summary>
public sealed record AdvanceLiveSoloRound(Guid RoundId, long TimelineRevision, DateTimeOffset At);
