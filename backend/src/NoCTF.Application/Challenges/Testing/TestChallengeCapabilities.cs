using System.Globalization;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Common;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Testing;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Challenges.Testing;

public enum TemplateCapabilityTestFailure : short
{
    NotFound, CapabilityMissing, InvalidConfiguration, InvalidInput, RuntimeNotReady,
    RuntimeScopeMismatch, Conflict, RequestConflict, ArchiveInvalid, ArchiveTooLarge
}

public sealed record ChallengeTestAccess(Guid ChallengeId, Guid ActorUserId, bool IsAdministrator, Guid? RuntimeInstanceId = null);
public sealed record ChallengeTestMaterial(Challenge Challenge, RuntimeInstance? Runtime, IReadOnlyList<ChallengeFlag> Flags);
public sealed record TemplateCapabilityTestAccepted(Guid ExecutionId, Guid RuntimeInstanceId);
public sealed record StartTemplateCapabilityTestCommand(ChallengeTestAccess Access, Guid RequestId,
    TemplateCapabilityTestKind Kind, Guid? FileId, string InputFingerprint, DateTimeOffset Now);

public sealed record TemplateCapabilityTestView(Guid ExecutionId, Guid RuntimeInstanceId, TemplateCapabilityTestKind Kind,
    TemplateCapabilityTestState State, DateTimeOffset AcceptedAt, DateTimeOffset Deadline, DateTimeOffset? CompletedAt,
    CapabilityExecutionFailure? Failure, TemplateCapabilityTestState? FailureStage, FixResult? FixResult, int? FixExitCode, bool FixExecutionUncertain,
    bool CheckCompleted, IReadOnlyList<CheckProbeEvidence> Probes)
{
    public ControlResult? ControlResult { get; init; }
    public string? ControllerIdentifier { get; init; }
    public DateTimeOffset? ControlObservedAt { get; init; }
}

public interface ITemplateCapabilityTestStore
{
    Task<OperationResult<ChallengeTestMaterial, TemplateCapabilityTestFailure>> ReadAsync(
        ChallengeTestAccess access, ChallengeCapabilityKind capability, CancellationToken ct);
    Task<OperationResult<TemplateCapabilityTestAccepted, TemplateCapabilityTestFailure>> StartAsync(StartTemplateCapabilityTestCommand command, CancellationToken ct);
    Task<TemplateCapabilityTestView?> FindAsync(ChallengeTestAccess access, Guid executionId, CancellationToken ct);
}

public sealed class TestChallengeCapabilities(ITemplateCapabilityTestStore store, TimeProvider clock)
{
    public async Task<OperationResult<BreakEvaluation, TemplateCapabilityTestFailure>> BreakAsync(
        ChallengeTestAccess access, string flag, CancellationToken ct)
    {
        var material = await store.ReadAsync(access, ChallengeCapabilityKind.Break, ct);
        if (!material.Succeeded) return Failure<BreakEvaluation>(material);
        var result = new EvaluateBreak().Execute(new(Guid.CreateVersion7(), flag,
            material.Value!.Flags.Select(candidate => new BreakCandidate(candidate.Id, candidate.Flag,
                candidate.FlagSha256, candidate.MatchKind)).ToArray(), clock.GetUtcNow(), CapabilityExecutionScope.TemplateTest));
        return result.Succeeded ? OperationResult<BreakEvaluation, TemplateCapabilityTestFailure>.Success(result.Value!)
            : OperationResult<BreakEvaluation, TemplateCapabilityTestFailure>.Failure(TemplateCapabilityTestFailure.InvalidInput, result.ErrorMessage!);
    }

    public async Task<OperationResult<QuestionnaireEvaluation, TemplateCapabilityTestFailure>> QuestionnaireAsync(
        ChallengeTestAccess access, IReadOnlyList<QuestionnaireAnswer> answers, CancellationToken ct)
    {
        var material = await store.ReadAsync(access, ChallengeCapabilityKind.Questionnaire, ct);
        if (!material.Succeeded) return Failure<QuestionnaireEvaluation>(material);
        var revision = material.Value!.Challenge.Definition!.Questionnaire!.CurrentRevision;
        var result = new EvaluateQuestionnaire().Execute(new(Guid.CreateVersion7(), revision,
            revision.Questions.OrderBy(question => question.Position).Select(question => question.Id).ToArray(), answers,
            clock.GetUtcNow(), CapabilityExecutionScope.TemplateTest));
        return result.Succeeded ? OperationResult<QuestionnaireEvaluation, TemplateCapabilityTestFailure>.Success(result.Value!)
            : OperationResult<QuestionnaireEvaluation, TemplateCapabilityTestFailure>.Failure(TemplateCapabilityTestFailure.InvalidInput, result.ErrorMessage!);
    }

    private static OperationResult<T, TemplateCapabilityTestFailure> Failure<T>(OperationResult<ChallengeTestMaterial, TemplateCapabilityTestFailure> result) =>
        OperationResult<T, TemplateCapabilityTestFailure>.Failure(result.FailureCode!.Value, result.ErrorMessage!);
}

public sealed record DispatchTemplateCapabilityTest(Guid RuntimeInstanceId, Guid ExecutionId);
public sealed record RunTemplateCapabilityTest(Guid RuntimeInstanceId, Guid ExecutionId, string RunnerId) : NoCTF.Application.Runtime.Instances.IRunnerNodeMessage;
public sealed record RecoverTemplateCapabilityTests;

public static class TemplateCapabilityTestBudget
{
    public const int PreparationAndCleanupSeconds = 300;
    public const int MaximumExecutionSeconds = PreparationAndCleanupSeconds + 3 * 3600;
    public const int HandlerSeconds = MaximumExecutionSeconds + 60;
    public const int AckExtensionSeconds = HandlerSeconds + 30;
}

public interface ITemplateCapabilityCheckResultStore
{
    Task<NoCTF.Application.GameplayFacts.Processing.InternalResultDisposition> RecordCheckAsync(Guid runtimeId, Guid executionId,
        string runnerId, IReadOnlyList<CheckProbeEvidence> probes, IReadOnlyList<CheckFlagContextEvidence> flagContexts,
        DateTimeOffset observedAt, CancellationToken ct);
}
