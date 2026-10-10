namespace NoCTF.Application.LiveSolo.Realtime;

public enum LiveSoloRealtimeAudience : short { Participant, Staff }
public sealed record LiveSoloRealtimeAccessRequest(string Key, Guid UserId, Guid CompetitionId, Guid MatchId, LiveSoloRealtimeAudience Audience);
public interface ILiveSoloRealtimeAccess
{
    Task<IReadOnlySet<string>> EligibleAsync(IReadOnlyList<LiveSoloRealtimeAccessRequest> requests, CancellationToken ct);
}
/// <summary>Invalidation only. Public program readers must never subscribe to current Match state.</summary>
public sealed record LiveSoloMatchChanged(Guid CompetitionId, Guid MatchId, DateTimeOffset OccurredAt);
public interface ILiveSoloRealtimePublisher
{
    Task PublishAsync(LiveSoloMatchChanged change, CancellationToken ct);
}
