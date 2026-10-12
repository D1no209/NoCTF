using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.Competitions.Awd;

public enum AwdCheckAdjudicationState : short { Applied, NotFound, Conflict, InvalidRequest, InvalidEvidence }

public sealed record AwdCheckAdjudicationCommand(Guid CompetitionId, Guid GameplayFactId,
    Guid? ExpectedAttemptId, DateTimeOffset? ExpectedReceivedAt, Guid ActorUserId, AwdCheckAdjudicationAction Action,
    string Reason, DateTimeOffset At);

public sealed record AwdCheckEvidenceView(Guid GameplayFactId,
    Guid CompetitionChallengeId, Guid TeamId, Guid RoundId, AwdFactDetails Evidence);

public sealed record AwdCheckAdjudicationResult(AwdCheckAdjudicationState State,
    AwdCheckEvidenceView? View = null);

public interface IAwdCheckAdjudicationStore
{
    Task<AwdCheckEvidenceView?> ReadAsync(Guid competitionId, Guid gameplayFactId, CancellationToken ct);
    Task<AwdCheckAdjudicationResult> ApplyAsync(AwdCheckAdjudicationCommand command, CancellationToken ct);
}

public sealed class AdjudicateAwdCheck(IAwdCheckAdjudicationStore store)
{
    public Task<AwdCheckAdjudicationResult> ExecuteAsync(AwdCheckAdjudicationCommand command,
        CancellationToken ct = default) => IsValid(command)
        ? store.ApplyAsync(command with { Reason = command.Reason.Trim() }, ct)
        : Task.FromResult(new AwdCheckAdjudicationResult(AwdCheckAdjudicationState.InvalidRequest));

    public static bool IsValid(AwdCheckAdjudicationCommand command) => command.CompetitionId != Guid.Empty
        && command.GameplayFactId != Guid.Empty
        && command.ActorUserId != Guid.Empty && Enum.IsDefined(command.Action)
        && !string.IsNullOrWhiteSpace(command.Reason) && command.Reason.Length <= 512;

    // The persistence adapter supplies a conclusion derived only from the saved server-owned manifest.
    public static AwdCheckAdjudicationState Apply(GameplayFact fact, AwdCheckAdjudicationCommand command,
        AwdHealthConclusion conclusion)
    {
        if (!IsValid(command)) return AwdCheckAdjudicationState.InvalidRequest;
        if (fact.CompetitionId != command.CompetitionId || fact.Id != command.GameplayFactId
            || fact.Kind != GameplayFactKind.AwdServiceTransition || fact.Awd is not { } evidence)
            return AwdCheckAdjudicationState.NotFound;
        if (evidence.AttemptId != command.ExpectedAttemptId || evidence.ReceivedAt != command.ExpectedReceivedAt
            || evidence.Settlement != AwdSettlementState.PendingAdjudication)
            return AwdCheckAdjudicationState.Conflict;
        if (command.Action == AwdCheckAdjudicationAction.AcceptEvidence)
        {
            if (!evidence.ManifestComplete || !evidence.EvidenceComplete || !conclusion.IsConclusive
                || evidence.AttemptId is null || evidence.AttemptStartedAt is not { } started
                || evidence.AttemptDeadline is not { } deadline || evidence.ObservedAt is not { } observed
                || evidence.ReceivedAt is not { } received || observed < started || observed > deadline
                || observed > received || received > command.At)
                return AwdCheckAdjudicationState.InvalidEvidence;
            evidence.Health = conclusion.Health;
            evidence.KnownTargetFailure = conclusion.KnownTargetFailure;
            evidence.Reason = conclusion.Reason;
            evidence.RequiredFlags = conclusion.RequiredFlags;
            evidence.ReadableFlags = conclusion.ReadableFlags;
            evidence.Execution = AwdCheckExecution.Completed;
            evidence.Settlement = AwdSettlementState.Final;
            fact.Result = AwdCheckerOutcomeMapper.ToGameplayFactResult(conclusion.Health);
        }
        else
        {
            // Exemption changes settlement, never invents a successful observation or flag custody.
            evidence.Settlement = AwdSettlementState.Exempted;
            fact.Result = null;
        }
        evidence.AdjudicatedAt = command.At;
        fact.State = GameplayFactState.Completed;
        fact.FailureCode = null;
        fact.UpdatedAt = command.At;
        fact.ConcurrencyStamp = Guid.NewGuid();
        return AwdCheckAdjudicationState.Applied;
    }
}
