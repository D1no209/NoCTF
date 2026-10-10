using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Media;

public enum LiveSoloMediaRole : short { Publisher, Judge, Director }
public enum LiveSoloMediaFailure : short { Unconfigured, Unavailable, Unauthorized, InvalidGeneration, Disabled }
public sealed record LiveSoloMediaAuthorization(Guid SessionId, Guid Generation, string RoomIdentity,
    string ParticipantIdentity, LiveSoloMediaRole Role, bool MaySubscribe, DateTimeOffset ExpiresAt);
public sealed record LiveSoloMediaToken(string ServerUrl, string Token, DateTimeOffset ExpiresAt);
public sealed record LiveSoloObservedScreen(string Identity, LiveSoloScreenState State, string? TrackId, DateTimeOffset ObservedAt, bool IsRecorder = false);
public sealed record LiveSoloRoomObservation(IReadOnlyList<LiveSoloObservedScreen> Screens, bool Exists = true);
public sealed record LiveSoloMediaReadiness(bool Configured, bool Available, bool EgressAvailable);

/// <summary>Provider-neutral media boundary. Business code never handles an SFU SDK type.</summary>
public interface ILiveSoloMediaGateway
{
    Task<LiveSoloMediaReadiness> CheckAsync(CancellationToken cancellationToken);
    Task CreateRoomAsync(string roomIdentity, CancellationToken cancellationToken);
    Task<LiveSoloMediaToken> AuthorizeAsync(LiveSoloMediaAuthorization authorization, CancellationToken cancellationToken);
    Task<LiveSoloRoomObservation> ObserveAsync(string roomIdentity, CancellationToken cancellationToken);
    Task StopRoomAsync(string roomIdentity, CancellationToken cancellationToken);
    Task DisconnectAsync(string roomIdentity, string identity, CancellationToken cancellationToken);
}
