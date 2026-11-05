using NoCTF.Domain.Shared;
using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Domain.Questionnaires.Sessions;

public enum QuestionnaireSessionState : short { Active, Submitted, Voided }
public enum QuestionnaireSubmissionKind : short { Manual, Timeout, ActivityFinished }

public sealed class QuestionnaireParticipant : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid ActivityId { get; set; }
    public Guid UserId { get; set; }
    public int IssuedCount { get; set; }
}

public sealed class QuestionnaireSession : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid ParticipantId { get; set; }
    public int Attempt { get; set; }
    public QuestionnaireSessionState State { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public QuestionnaireSubmissionKind? SubmissionKind { get; set; }
    public Guid? SubmittedById { get; set; }
    public List<QuestionnaireSessionQuestion> Items { get; set; } = [];
    public List<QuestionnaireAssignment> Assignments { get; set; } = [];
}

/// <summary>One active paper per participant, enforced without a filtered index.</summary>
public sealed class QuestionnaireActiveSession
{
    public Guid ParticipantId { get; set; }
    public Guid SessionId { get; set; }
}

public sealed class QuestionnaireIssueReceipt
{
    public Guid ParticipantId { get; set; }
    public Guid RequestId { get; set; }
    public Guid SessionId { get; set; }
}

public sealed class QuestionnaireSessionQuestion : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid AssignmentId { get; set; }
    public Guid QuestionnaireRevisionId { get; set; }
    public Guid QuestionnaireQuestionId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid RuleId { get; set; }
    public int Order { get; set; }
    public int Points { get; set; }
    [NotMapped] public QuestionnaireAnswerResult? Result { get; set; }
    public Guid? AppliedGradingRevision { get; set; }
    public Guid? AnsweredById { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public List<QuestionnaireSessionOption> Options { get; set; } = [];
}

/// <summary>Only stable option identities are accepted; display letters are never answer keys.</summary>
public sealed class QuestionnaireSessionOption
{
    public Guid ItemId { get; set; }
    public Guid QuestionVersionId { get; set; }
    public Guid OptionId { get; set; }
    public int Order { get; set; }
    public bool Selected { get; set; }
}

public sealed class QuestionnaireDrawReceipt
{
    public Guid ParticipantId { get; set; }
    public Guid SourceId { get; set; }
    public Guid SessionId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
}

/// <summary>Idempotency for answer writes, with no answer content in transport logs.</summary>
public sealed class QuestionnaireAnswerReceipt
{
    public Guid ItemId { get; set; }
    public Guid RequestId { get; set; }
    public Guid ActorId { get; set; }
    public Guid ResultingRevision { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public byte[] SelectionDigest { get; set; } = [];
}
