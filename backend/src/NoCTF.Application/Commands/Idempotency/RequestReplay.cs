namespace NoCTF.Application.Commands.Idempotency;

using System.Text.Json.Serialization;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Commands;

public sealed record ReplayScope(Guid UserId, ReplayOperation Operation, Guid CompetitionId, Guid ResourceId);
public sealed record RuntimeCommandReceipt(Guid RuntimeInstanceId);
public interface IRequestCommandKey { Guid? Key { get; } Guid? ActorId => null; }
public sealed class RequestReplayConflictException : Exception
{
    public RequestReplayConflictException() : base("The request identifier was already used with different input.") { }
}
public interface IRequestReplay
{
    Task<T?> FindAsync<T>(ReplayScope scope, ReplayFingerprintInput input, CancellationToken ct)
        where T : class;
    void Store<T>(T response) where T : class;
    bool Replayed { get; }
    Guid? ActorId { get; }
}

public abstract record ReplayFingerprintInput;
public sealed record EmptyReplayFingerprint : ReplayFingerprintInput;
public sealed record RuntimeReplayFingerprint(RuntimeAction Action, TimeSpan? Extension)
    : ReplayFingerprintInput;
public sealed record ScopedRuntimeReplayFingerprint(RuntimeAction Action, Guid TeamId, Guid? ExpectedRuntimeInstanceId) : ReplayFingerprintInput;
public sealed record FlagReplayFingerprint(IReadOnlyList<string> Flags)
    : ReplayFingerprintInput;
public sealed record ManualAdjustmentReplayFingerprint(Guid TeamId, int Delta)
    : ReplayFingerprintInput;
public sealed record AdminRuntimeReplayFingerprint(
    [property: JsonPropertyName("teamId")] Guid? TeamId,
    [property: JsonPropertyName("action")] RuntimeAction Action,
    [property: JsonPropertyName("extension")] TimeSpan? Extension)
    : ReplayFingerprintInput;
public sealed record PatchUploadReplayFingerprint(
    [property: JsonPropertyName("competitionChallengeId")] Guid CompetitionChallengeId,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("contentType")] string ContentType,
    long Length,
    string Sha256)
    : ReplayFingerprintInput;

[JsonSerializable(typeof(EmptyReplayFingerprint))]
[JsonSerializable(typeof(RuntimeReplayFingerprint))]
[JsonSerializable(typeof(ScopedRuntimeReplayFingerprint))]
[JsonSerializable(typeof(FlagReplayFingerprint))]
[JsonSerializable(typeof(ManualAdjustmentReplayFingerprint))]
[JsonSerializable(typeof(AdminRuntimeReplayFingerprint))]
[JsonSerializable(typeof(PatchUploadReplayFingerprint))]
public partial class ReplayFingerprintJsonContext : JsonSerializerContext;
