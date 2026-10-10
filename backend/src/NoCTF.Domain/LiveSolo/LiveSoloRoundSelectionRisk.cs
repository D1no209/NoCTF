namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloQuestionSelectionKind : short { Automatic, StaffSpecified }
public enum LiveSoloSelectionRiskKind : short { SeenByTeam, SeenByRosterMember, SameSource, PreviouslyUsed, PublicExposure }
public enum LiveSoloResourceFailure : short { None, NotFound, ResourceCapacityExceeded, UnsupportedEnvironment, IsolationNotConfigured, IsolationVerificationFailed, DependencyUnavailable }

public sealed class LiveSoloRoundSelectionRisk
{
    public Guid RoundId { get; set; }
    public int Position { get; set; }
    public int SelectionPosition { get; set; }
    public LiveSoloSelectionRiskKind Kind { get; set; }
    public Guid? CompetitionChallengeId { get; set; }
    public Guid? CanonicalChallengeId { get; set; }
}

public sealed class LiveSoloRoundSelectionAudit
{
    public Guid RoundId { get; set; }
    public int Position { get; set; }
    public LiveSoloQuestionSelectionKind Kind { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid QuestionGroupId { get; set; }
    public Guid QuestionGroupStamp { get; set; }
    public Guid SelectionRevision { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
