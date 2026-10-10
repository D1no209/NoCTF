using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Media;

public sealed record LiveSoloRecordingView(Guid Id, Guid MediaSessionId, Guid? RoundId, Guid UserId, string UserName,
    Guid? TeamId, string? TeamName, LiveSoloRecordingState State, Guid ConcurrencyStamp, DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, DateTimeOffset KeepUntil, bool DisputeHold, bool Published,
    long? ByteLength, LiveSoloRecordingFailure? Failure = null, int Chunk = 0);
public sealed record LiveSoloRecordingPage(IReadOnlyList<LiveSoloRecordingView> Items, int Total, bool CanJudge, bool CanPublish);
public sealed record ChangeLiveSoloRecording(Guid CompetitionId, Guid MatchId, Guid RecordingId, Guid ActorId,
    Guid ExpectedStamp, LiveSoloRecordingAction Action, string Reason);
public sealed record LiveSoloRecordingChangeResult(LiveSoloRecordingView? Recording, LiveSoloFailure? Failure = null);
public sealed record LiveSoloRecordingContent(Guid FileId, Stream Content, string ContentType, string FileName);
public sealed record LiveSoloRecordingDecisionView(Guid Id, Guid RecordingId, Guid ActorUserId, LiveSoloRecordingAction Action,
    string Reason, DateTimeOffset OccurredAt, bool PreviousHold, bool Hold, bool PreviousPublished, bool Published,
    LiveSoloRecordingState? PreviousState = null, LiveSoloRecordingState? State = null, Guid? ReplacementRecordingId = null);
public interface ILiveSoloRecordingRecovery
{
    Task<LiveSoloRecordingChangeResult> RecoverAsync(ChangeLiveSoloRecording command,CancellationToken ct);
}

public interface ILiveSoloRecordingStore
{
    Task<LiveSoloRecordingPage?> ListAsync(Guid competitionId, Guid matchId, Guid actorId, bool staff, int offset, int limit, CancellationToken ct);
    Task<LiveSoloRecordingChangeResult> ChangeAsync(ChangeLiveSoloRecording command, CancellationToken ct);
    Task<LiveSoloRecordingView?> ReadRecordingAsync(Guid competitionId,Guid matchId,Guid recordingId,Guid actorId,CancellationToken ct);
    Task<LiveSoloRecordingContent?> OpenAsync(Guid competitionId, Guid matchId, Guid recordingId, Guid actorId, CancellationToken ct);
    Task<bool> MayOpenAsync(Guid competitionId, Guid matchId, Guid recordingId, Guid actorId, CancellationToken ct);
    Task<Guid?> AuthorizeFileAsync(Guid competitionId, Guid matchId, Guid recordingId, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<LiveSoloRecordingDecisionView>?> DecisionsAsync(Guid competitionId, Guid matchId, Guid recordingId, Guid actorId, CancellationToken ct);
}

public sealed class ManageLiveSoloRecordings(ILiveSoloRecordingStore store, ILiveSoloRecordingRecovery recovery)
{
    public Task<LiveSoloRecordingPage?> ListAsync(Guid competitionId, Guid matchId, Guid actorId, bool staff, int offset, int limit, CancellationToken ct)
        => store.ListAsync(competitionId, matchId, actorId, staff, Math.Max(0, offset), Math.Clamp(limit, 1, 100), ct);
    public Task<LiveSoloRecordingChangeResult> ChangeAsync(ChangeLiveSoloRecording command, CancellationToken ct)
    {
        if (!Enum.IsDefined(command.Action) || command.ExpectedStamp == Guid.Empty || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > 4000)
            return Task.FromResult(new LiveSoloRecordingChangeResult(null, LiveSoloFailure.InvalidConfiguration));
        var normalized=command with {Reason=command.Reason.Trim()};
        return command.Action is LiveSoloRecordingAction.RetryPendingStart or LiveSoloRecordingAction.ReconcileExport
            or LiveSoloRecordingAction.RetryArchive or LiveSoloRecordingAction.StartNewChunk
            ? recovery.RecoverAsync(normalized,ct)
            : store.ChangeAsync(normalized,ct);
    }
}
