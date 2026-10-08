using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.Infrastructure.LiveSolo.Media;

/// <summary>Explicit disabled adapter; missing media never creates a false sharing/readiness proof.</summary>
public sealed class UnconfiguredLiveSoloMediaGateway : ILiveSoloMediaGateway
{
    public Task<LiveSoloMediaReadiness> CheckAsync(CancellationToken ct) => Task.FromResult(new LiveSoloMediaReadiness(false, false, false));
    public Task CreateRoomAsync(string room, CancellationToken ct) => Task.FromException(new InvalidOperationException("LiveSolo media is unconfigured."));
    public Task<LiveSoloMediaToken> AuthorizeAsync(LiveSoloMediaAuthorization authorization, CancellationToken ct) => Task.FromException<LiveSoloMediaToken>(new InvalidOperationException("LiveSolo media is unconfigured."));
    public Task<LiveSoloRoomObservation> ObserveAsync(string room, CancellationToken ct) => Task.FromException<LiveSoloRoomObservation>(new InvalidOperationException("LiveSolo media is unconfigured."));
    public Task StopRoomAsync(string room, CancellationToken ct) => Task.CompletedTask;
    public Task DisconnectAsync(string room, string identity, CancellationToken ct) => Task.CompletedTask;
}
