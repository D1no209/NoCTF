using NoCTF.Application.GameplayFacts.Intake;

namespace NoCTF.Application.Challenges.Actions;

public enum ChallengeActionKind { SubmitFlag, PrepareFixTarget, SubmitFix, ViewCheck, ViewControl }
public enum ChallengeActionAvailability { Available, AdmissionRejected, TargetNotRunning, TargetBusy, RecoveryRequired, TargetRequired }
public sealed record AvailableChallengeAction(ChallengeActionKind Kind, bool Enabled, ChallengeActionAvailability Availability,
    GameplayFactAdmissionFailureCode? AdmissionFailure = null, Guid? RuntimeInstanceId = null, int? RemainingAttempts = null,
    Guid? GameplayFactId = null);

public interface IAvailableChallengeActionsReader
{
    Task<IReadOnlyList<AvailableChallengeAction>> ReadAsync(Guid competitionId, Guid challengeId, Guid actorId, DateTimeOffset now, CancellationToken ct);
}

public sealed class GetAvailableChallengeActions(IAvailableChallengeActionsReader reader)
{
    public Task<IReadOnlyList<AvailableChallengeAction>> ExecuteAsync(Guid competitionId, Guid challengeId, Guid actorId, DateTimeOffset now, CancellationToken ct) =>
        actorId == Guid.Empty ? Task.FromResult<IReadOnlyList<AvailableChallengeAction>>([]) : reader.ReadAsync(competitionId, challengeId, actorId, now, ct);
}
