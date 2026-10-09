using NoCTF.Domain.Identity.Mfa;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Media;

public sealed record LiveSoloMediaMemberView(Guid UserId, Guid TeamId, LiveSoloSide Side, LiveSoloScreenState State, string Identity, string UserName);
public sealed record LiveSoloMediaView(Guid Id, Guid MatchId, Guid Generation, LiveSoloMediaState State,
    bool ParticipantsMayViewOpponents, IReadOnlyList<LiveSoloMediaMemberView> Members);
public sealed record LiveSoloMediaResult(LiveSoloMediaView? Session, LiveSoloMediaToken? Token = null, LiveSoloMediaFailure? Failure = null);
public sealed record PrepareLiveSoloMedia(Guid CompetitionId, Guid MatchId, Guid ActorId, Guid ExpectedMatchStamp);
public sealed record JoinLiveSoloMedia(Guid CompetitionId, Guid MatchId, Guid ActorId, Guid Generation, LiveSoloMediaRole Role,
    int TokenVersion, AuthenticationContext? Authentication);
public sealed record RefreshLiveSoloMedia(Guid SessionId, string RoomIdentity);
public interface ILiveSoloMediaStore
{
    Task<LiveSoloMediaView?> ReadAsync(Guid competitionId, Guid matchId, Guid actorId, CancellationToken ct);
    Task<LiveSoloMediaResult> PrepareAsync(PrepareLiveSoloMedia command, CancellationToken ct);
    Task<LiveSoloMediaResult> JoinAsync(JoinLiveSoloMedia command, CancellationToken ct);
    Task RefreshAsync(RefreshLiveSoloMedia command, CancellationToken ct);
}
public sealed class ManageLiveSoloMedia(ILiveSoloMediaStore store)
{
    public Task<LiveSoloMediaResult> PrepareAsync(PrepareLiveSoloMedia command, CancellationToken ct) =>
        command.ExpectedMatchStamp == Guid.Empty ? Task.FromResult(new LiveSoloMediaResult(null, Failure: LiveSoloMediaFailure.InvalidGeneration))
            : store.PrepareAsync(command, ct);
    public Task<LiveSoloMediaResult> JoinAsync(JoinLiveSoloMedia command, CancellationToken ct) =>
        !Enum.IsDefined(command.Role) || command.Generation == Guid.Empty || command.Authentication is not { IsWellFormed: true }
        ? Task.FromResult(new LiveSoloMediaResult(null, Failure: LiveSoloMediaFailure.Unauthorized)) : store.JoinAsync(command, ct);
}
