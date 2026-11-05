using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Application.Challenges.Capabilities;

public enum CapabilityExecutionScope : short { GameplayFact, TemplateTest }

public abstract record CapabilityExecutionResult
{
    public Guid ExecutionId { get; init; }
    public CapabilityExecutionScope Scope { get; init; }
    public Guid? RuntimeInstanceId { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
    public CapabilityExecutionState ExecutionState { get; init; } = CapabilityExecutionState.Completed;
    public CapabilityExecutionFailure? ExecutionFailure { get; init; }

    protected T Bind<T>(T outcome) where T : CapabilityOutcome
    {
        if (Scope != CapabilityExecutionScope.GameplayFact || ExecutionId == Guid.Empty)
            throw new InvalidOperationException("Only a formal execution can be bound to a gameplay fact.");
        outcome.GameplayFactId = ExecutionId;
        outcome.RuntimeInstanceId = RuntimeInstanceId;
        outcome.ObservedAt = ObservedAt;
        outcome.ExecutionState = ExecutionState;
        outcome.ExecutionFailure = ExecutionFailure;
        return outcome;
    }
}

public sealed record BreakMatchEvidence(Guid FlagId, Guid? OwnerTeamId);

public sealed record BreakEvaluation : CapabilityExecutionResult
{
    public BreakMatchResult Match { get; init; }
    public IReadOnlyList<BreakMatchEvidence> Flags { get; init; } = [];

    public BreakOutcome ToGameplayOutcome() => Bind(new BreakOutcome
    {
        Match = Match,
        Flags = Flags.Select(flag => new BreakFlagMatch
        {
            GameplayFactId = ExecutionId, FlagId = flag.FlagId, OwnerTeamId = flag.OwnerTeamId
        }).ToList()
    });
}

public sealed record FixExecutionResult : CapabilityExecutionResult
{
    public FixResult? Result { get; init; }
    public int? ExitCode { get; init; }
    public bool ExecutionUncertain { get; init; }
    public string? ExecutionIdentity { get; init; }

    public FixOutcome ToGameplayOutcome() => Bind(new FixOutcome
    {
        Result = Result, ExitCode = ExitCode, ExecutionUncertain = ExecutionUncertain,
        ExecutionIdentity = ExecutionIdentity
    });
}

public sealed record ControlEvaluation : CapabilityExecutionResult
{
    public ControlResult Result { get; init; }
    public string? ControllerIdentifier { get; init; }
    public override string ToString() => $"ControlEvaluation {{ ExecutionId = {ExecutionId}, Scope = {Scope}, Result = {Result}, ControllerIdentifier = [REDACTED] }}";

    public ControlOutcome ToGameplayOutcome() => Bind(new ControlOutcome
    {
        Result = Result, ControllerIdentifier = ControllerIdentifier
    });
}

public sealed record CheckEvaluation : CapabilityExecutionResult
{
    public IReadOnlyList<CheckProbeEvidence> Probes { get; init; } = [];

    public CheckOutcome ToGameplayOutcome() => Bind(new CheckOutcome
    {
        Probes = Probes.Select((probe, position) => new CheckProbe
        {
            GameplayFactId = ExecutionId, Position = position, Kind = probe.Kind, FlagId = probe.FlagId,
            Result = probe.Result, Reason = probe.Reason
        }).ToList()
    });
}

public sealed record QuestionnaireQuestionEvidence(Guid QuestionId, QuestionnaireAnswerResult Result);

public sealed record QuestionnaireEvaluation : CapabilityExecutionResult
{
    public Guid RevisionId { get; init; }
    public IReadOnlyList<QuestionnaireQuestionEvidence> Questions { get; init; } = [];

    public QuestionnaireOutcome ToGameplayOutcome() => Bind(new QuestionnaireOutcome
    {
        RevisionId = RevisionId,
        Questions = Questions.Select(question => new QuestionnaireQuestionResult
        {
            GameplayFactId = ExecutionId, QuestionId = question.QuestionId, Result = question.Result
        }).ToList()
    });
}
