using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Media;

public sealed record LiveSoloProgramHealth(Guid Id,Guid ConcurrencyStamp,LiveSoloCaptureState State,bool Stalled,
    DateTimeOffset? LastFragmentImportedAt,bool RotationRequested);
public sealed record RecoverLiveSoloProgram(Guid CompetitionId,Guid MatchId,Guid ActorId,Guid ProgramId,Guid ExpectedStamp,
    LiveSoloProgramAction Action,string Reason);
public sealed record LiveSoloProgramRecoveryResult(LiveSoloProgramHealth? Program,LiveSoloFailure? Failure=null);
public sealed record LiveSoloProgramDecisionView(Guid Id,Guid ProgramId,LiveSoloProgramAction Action,string Reason,DateTimeOffset OccurredAt,
    LiveSoloCaptureState PreviousState,LiveSoloCaptureState State);
public interface ILiveSoloProgramRecovery
{
    Task<LiveSoloProgramHealth?> HealthAsync(Guid competitionId,Guid matchId,Guid actorId,CancellationToken ct);
    Task<LiveSoloProgramRecoveryResult> RecoverAsync(RecoverLiveSoloProgram command,CancellationToken ct);
    Task<IReadOnlyList<LiveSoloProgramDecisionView>?> ProgramDecisionsAsync(Guid competitionId,Guid matchId,Guid actorId,CancellationToken ct);
}
public sealed class ManageLiveSoloProgram(ILiveSoloProgramRecovery recovery)
{
    public Task<LiveSoloProgramRecoveryResult> RecoverAsync(RecoverLiveSoloProgram command,CancellationToken ct)=>
        !Enum.IsDefined(command.Action)||command.ProgramId==Guid.Empty||command.ExpectedStamp==Guid.Empty
            ||string.IsNullOrWhiteSpace(command.Reason)||command.Reason.Trim().Length>4000
            ? Task.FromResult(new LiveSoloProgramRecoveryResult(null,LiveSoloFailure.InvalidConfiguration))
            : recovery.RecoverAsync(command with {Reason=command.Reason.Trim()},ct);
}
