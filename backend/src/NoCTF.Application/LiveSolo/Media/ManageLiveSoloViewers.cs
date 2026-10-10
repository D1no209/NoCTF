namespace NoCTF.Application.LiveSolo.Media;

public enum LiveSoloViewerAction : short { Enter, Renew, Leave }
public enum LiveSoloViewerFailure : short { Unavailable, CapacityReached, Expired, Conflict }
public sealed record LiveSoloViewerAdmission(Guid Id, DateTimeOffset ExpiresAt);
public sealed record LiveSoloViewerResult(LiveSoloViewerAdmission? Admission, LiveSoloViewerFailure? Failure = null);
public interface ILiveSoloViewerStore
{
    Task<LiveSoloViewerResult> EnterAsync(Guid competitionId, Guid matchId, Guid actorId, Guid? existingLeaseId, CancellationToken ct);
    Task<LiveSoloViewerResult> RenewAsync(Guid competitionId, Guid matchId, Guid actorId, Guid leaseId, CancellationToken ct);
    Task LeaveAsync(Guid competitionId, Guid matchId, Guid actorId, Guid leaseId, CancellationToken ct);
}
