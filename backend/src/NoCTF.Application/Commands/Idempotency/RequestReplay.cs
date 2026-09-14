namespace NoCTF.Application.Commands.Idempotency;

public enum ReplayOperation { FlagSubmission, ManualAdjustment, PatchUpload, RuntimeMutation, AdminRuntimeMutation, TemplateTestRuntimeMutation, AwdpDefenseTarget, PatchVerificationTarget }
public sealed record ReplayScope(Guid UserId, ReplayOperation Operation, Guid CompetitionId, Guid ResourceId);
public sealed record RuntimeCommandReceipt(Guid RuntimeInstanceId);
public interface IRequestCommandKey { Guid? Key { get; } Guid? ActorId => null; }
public sealed class RequestReplayConflictException : Exception
{
    public RequestReplayConflictException() : base("The request identifier was already used with different input.") { }
}
public interface IRequestReplay
{
    Task<T?> FindAsync<T>(ReplayScope scope, object input, CancellationToken ct) where T : class;
    void Store<T>(T response) where T : class;
    bool Replayed { get; }
    Guid? ActorId { get; }
}
