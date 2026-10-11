using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Theory.Papers;

public enum TheoryParticipantKind : short { Individual, Team }
public enum TheoryPaperState : short { Active, Submitted, Voided }
public enum TheorySubmissionKind : short { Manual, Timeout, ActivityFinished }
public enum TheoryAnswerResult : short { Correct, Wrong, Unanswered, FullCredit }

public sealed class TheoryParticipant : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid ActivityId { get; set; }
    public TheoryParticipantKind Kind { get; set; }
    public Guid? UserId { get; set; }
    public Guid? TeamId { get; set; }
    public int IssuedCount { get; set; }
}

public sealed class TheoryPaper : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid ParticipantId { get; set; }
    public int Attempt { get; set; }
    public TheoryPaperState State { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public TheorySubmissionKind? SubmissionKind { get; set; }
    public Guid? SubmittedById { get; set; }
    public List<TheoryPaperItem> Items { get; set; } = [];
}

/// <summary>One active paper per participant, enforced without a filtered index.</summary>
public sealed class TheoryActivePaper
{
    public Guid ParticipantId { get; set; }
    public Guid PaperId { get; set; }
}

public sealed class TheoryIssueReceipt
{
    public Guid ParticipantId { get; set; }
    public Guid RequestId { get; set; }
    public Guid PaperId { get; set; }
}

public sealed class TheoryPaperItem : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid PaperId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid RuleId { get; set; }
    public int Order { get; set; }
    public int Points { get; set; }
    public TheoryAnswerResult? Result { get; set; }
    public Guid? AppliedGradingRevision { get; set; }
    public Guid? AnsweredById { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public List<TheoryPaperOption> Options { get; set; } = [];
}

/// <summary>Only stable option identities are accepted; display letters are never answer keys.</summary>
public sealed class TheoryPaperOption
{
    public Guid ItemId { get; set; }
    public Guid OptionId { get; set; }
    public int Order { get; set; }
    public bool Selected { get; set; }
}

public sealed class TheoryDrawReceipt
{
    public Guid ParticipantId { get; set; }
    public Guid SourceId { get; set; }
    public Guid PaperId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
}

/// <summary>Idempotency for answer writes, with no answer content in transport logs.</summary>
public sealed class TheoryAnswerReceipt
{
    public Guid ItemId { get; set; }
    public Guid RequestId { get; set; }
    public Guid ActorId { get; set; }
    public Guid ResultingRevision { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public byte[] SelectionDigest { get; set; } = [];
}
