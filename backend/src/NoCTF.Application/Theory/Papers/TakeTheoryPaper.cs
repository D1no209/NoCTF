using NoCTF.Application.Theory.Activities;
using NoCTF.Domain.Theory.Activities;

namespace NoCTF.Application.Theory.Papers;

public sealed record TheoryAnswerCommand(Guid ActorId, Guid ActivityId, Guid PaperId, Guid ItemId, Guid ExpectedRevision,
    Guid RequestId, IReadOnlyList<Guid> OptionIds, DateTimeOffset Now);
public sealed record TheoryPaperCommand(Guid ActorId, Guid ActivityId, Guid? PaperId, Guid? ExpectedRevision, DateTimeOffset Now,
    Guid? RequestId = null, int ExpectedIssuedCount = 0);
public sealed record TheoryPaperHistory(Guid PaperId, int Attempt, NoCTF.Domain.Theory.Papers.TheoryPaperState State, long? Score, DateTimeOffset? SubmittedAt);
public sealed record TheoryPaperView(TheoryActivitySummary Activity, TheoryDeadline Deadline, TheoryParticipantPaper? Paper,
    int IssuedCount, TheoryEffectiveScore EffectiveScore, IReadOnlyList<TheoryPaperHistory> History)
{
    public DateTimeOffset ServerTime { get; init; }
    public bool CanIssue { get; init; }
    public bool CanAnswer { get; init; }
}
public sealed record TheoryPaperResult(TheoryPaperView? View = null, TheoryPaperFailure? Failure = null,
    IReadOnlyList<TheoryAllocationShortage>? Shortages = null);
public interface ITheoryPaperStore
{
    Task<TheoryPaperResult> ReadAsync(TheoryPaperCommand command, CancellationToken ct);
    Task<TheoryPaperResult> IssueAsync(TheoryPaperCommand command, CancellationToken ct);
    Task<TheoryPaperResult> AnswerAsync(TheoryAnswerCommand command, CancellationToken ct);
    Task<TheoryPaperResult> SubmitAsync(TheoryPaperCommand command, CancellationToken ct);
    Task SweepAsync(Guid activityId, DateTimeOffset now, CancellationToken ct);
}

public sealed record SweepTheoryActivity(Guid ActivityId);
