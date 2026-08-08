using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Competitions.Events;

public enum CompetitionEventKind : short
{
    CompetitionCreated,
    CompetitionUpdated,
    CompetitionDeleted,
    CompetitionLifecycleChanged,
    LeaderboardVisibilityChanged,
    ChallengeCreated,
    ChallengeUpdated,
    ChallengePublished,
    ChallengeUnpublished,
    ChallengeDeleted,
    HintPublished,
    HintUnlocked,
    TeamRegistered,
    TeamRegistrationChanged,
    TeamUpdated,
    TeamDeleted,
    TeamMemberJoined,
    TeamMemberRemoved,
    TeamCaptainTransferred,
    TeamBanned,
    TeamUnbanned,
    SubmissionReceived,
    SubmissionEvaluated,
    ScoringRecorded,
    FirstBloodAwarded,
    SecondBloodAwarded,
    ThirdBloodAwarded,
    RuntimeCreated,
    RuntimeStateChanged,
    RuntimeExtended,
    RuntimeReset,
    RuntimePortAllocated,
    ProtectedSubmissionFlagAccessed,
    CheatIncidentDetected,
    CheatIncidentConfirmed,
    CheatIncidentDismissed,
    CheatIncidentSuperseded,
    CheatIncidentCorrected,
    ProtectedCompetitionExportCreated,
    TeamBanAppealSubmitted,
    TeamBanAppealUpheld,
    TeamBanAppealAccepted,
    TeamBanCorrectionPublished
}

public enum CompetitionEventLevel : short
{
    Information,
    Warning,
    Error
}

public enum CompetitionEventVisibility : short
{
    Public,
    Team,
    Staff
}

public sealed class CompetitionEvent
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public CompetitionEventKind Kind { get; set; }
    public CompetitionEventLevel Level { get; set; }
    public CompetitionEventVisibility Visibility { get; set; }
    public Guid? ActorUserId { get; set; }
    public EntityReferenceKind SubjectType { get; set; }
    public Guid SubjectId { get; set; }
    public EntityReferenceKind? RelatedType { get; set; }
    public Guid? RelatedId { get; set; }
    public Guid? ParentEventId { get; set; }
    public string PayloadJson { get; set; } = "{\"schemaVersion\":1}";
    public DateTimeOffset OccurredAt { get; set; }

    [NotMapped] public Guid? RelatedUserId => Reference(EntityReferenceKind.User);
    [NotMapped] public Guid? TeamId => Reference(EntityReferenceKind.Team);
    [NotMapped] public Guid? CompetitionChallengeId => Reference(EntityReferenceKind.CompetitionChallenge);
    [NotMapped] public Guid? HintId => Reference(EntityReferenceKind.ChallengeHint);
    [NotMapped] public Guid? RuntimeInstanceId => Reference(EntityReferenceKind.RuntimeInstance);
    [NotMapped] public Guid? SubmissionId => Reference(EntityReferenceKind.Submission);
    [NotMapped] public Guid? ScoringEventId => Reference(EntityReferenceKind.ScoringEvent);
    [NotMapped] public Guid? QuestionId => Reference(EntityReferenceKind.Notification);
    [NotMapped] public CompetitionStatus? CompetitionStatus => Payload<CompetitionStatus>("competitionStatus");
    [NotMapped] public CompetitionLeaderboardVisibility? LeaderboardVisibility => Payload<CompetitionLeaderboardVisibility>("leaderboardVisibility");
    [NotMapped] public NoCTF.Domain.Teams.TeamRegistrationStatus? TeamRegistrationStatus => Payload<NoCTF.Domain.Teams.TeamRegistrationStatus>("teamRegistrationStatus");
    [NotMapped] public NoCTF.Domain.Submissions.SubmissionKind? SubmissionKind => Payload<NoCTF.Domain.Submissions.SubmissionKind>("submissionKind");
    [NotMapped] public NoCTF.Domain.Submissions.SubmissionEvaluationState? SubmissionState => Payload<NoCTF.Domain.Submissions.SubmissionEvaluationState>("submissionState");
    [NotMapped] public NoCTF.Domain.Submissions.ScoringEventKind? ScoringEventKind => Payload<NoCTF.Domain.Submissions.ScoringEventKind>("scoringEventKind");
    [NotMapped] public NoCTF.Domain.Submissions.ScoringResult? ScoringResult => Payload<NoCTF.Domain.Submissions.ScoringResult>("scoringResult");
    [NotMapped] public NoCTF.Domain.Runtime.RuntimeState? RuntimeState => Payload<NoCTF.Domain.Runtime.RuntimeState>("runtimeState");
    [NotMapped] public NoCTF.Domain.Challenges.Questions.CompetitionQuestionStatus? QuestionStatus => Payload<NoCTF.Domain.Challenges.Questions.CompetitionQuestionStatus>("questionStatus");
    [NotMapped] public int? RuntimeGeneration => Payload<int>("runtimeGeneration");
    [NotMapped] public int? HostPort => Payload<int>("hostPort");
    [NotMapped] public string? Reason => Payload<string>("reason");

    private Guid? Reference(EntityReferenceKind kind) =>
        SubjectType == kind ? SubjectId : RelatedType == kind ? RelatedId : null;

    private T? Payload<T>(string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(PayloadJson);
            return document.RootElement.TryGetProperty(propertyName, out var value)
                && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? value.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })
                : default;
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
