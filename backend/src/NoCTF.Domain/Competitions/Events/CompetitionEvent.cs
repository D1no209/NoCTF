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

    [NotMapped] public Guid? RelatedUserId => Reference(EntityReferenceKind.User) ?? PayloadValue<Guid>("relatedUserId");
    [NotMapped] public Guid? TeamId => Reference(EntityReferenceKind.Team) ?? PayloadValue<Guid>("teamId");
    [NotMapped] public Guid? CompetitionChallengeId => Reference(EntityReferenceKind.CompetitionChallenge) ?? PayloadValue<Guid>("competitionChallengeId");
    [NotMapped] public Guid? HintId => Reference(EntityReferenceKind.ChallengeHint) ?? PayloadValue<Guid>("hintId");
    [NotMapped] public Guid? RuntimeInstanceId => Reference(EntityReferenceKind.RuntimeInstance) ?? PayloadValue<Guid>("runtimeInstanceId");
    [NotMapped] public Guid? SubmissionId => Reference(EntityReferenceKind.Submission) ?? PayloadValue<Guid>("submissionId");
    [NotMapped] public Guid? ScoringEventId => Reference(EntityReferenceKind.ScoringEvent) ?? PayloadValue<Guid>("scoringEventId");
    [NotMapped] public Guid? QuestionId => Reference(EntityReferenceKind.Notification) ?? PayloadValue<Guid>("questionId");
    [NotMapped] public CompetitionStatus? CompetitionStatus => PayloadValue<CompetitionStatus>("competitionStatus");
    [NotMapped] public CompetitionLeaderboardVisibility? LeaderboardVisibility => PayloadValue<CompetitionLeaderboardVisibility>("leaderboardVisibility");
    [NotMapped] public NoCTF.Domain.Teams.TeamRegistrationStatus? TeamRegistrationStatus => PayloadValue<NoCTF.Domain.Teams.TeamRegistrationStatus>("teamRegistrationStatus");
    [NotMapped] public NoCTF.Domain.Submissions.SubmissionKind? SubmissionKind => PayloadValue<NoCTF.Domain.Submissions.SubmissionKind>("submissionKind");
    [NotMapped] public NoCTF.Domain.Submissions.SubmissionEvaluationState? SubmissionState => PayloadValue<NoCTF.Domain.Submissions.SubmissionEvaluationState>("submissionState");
    [NotMapped] public NoCTF.Domain.Submissions.ScoringEventKind? ScoringEventKind => PayloadValue<NoCTF.Domain.Submissions.ScoringEventKind>("scoringEventKind");
    [NotMapped] public NoCTF.Domain.Submissions.ScoringResult? ScoringResult => PayloadValue<NoCTF.Domain.Submissions.ScoringResult>("scoringResult");
    [NotMapped] public NoCTF.Domain.Runtime.RuntimeState? RuntimeState => PayloadValue<NoCTF.Domain.Runtime.RuntimeState>("runtimeState");
    [NotMapped] public NoCTF.Domain.Challenges.Questions.CompetitionQuestionStatus? QuestionStatus => PayloadValue<NoCTF.Domain.Challenges.Questions.CompetitionQuestionStatus>("questionStatus");
    [NotMapped] public int? RuntimeGeneration => PayloadValue<int>("runtimeGeneration");
    [NotMapped] public int? HostPort => PayloadValue<int>("hostPort");
    [NotMapped] public string? Reason => PayloadText("reason");

    private Guid? Reference(EntityReferenceKind kind) =>
        SubjectType == kind ? SubjectId : RelatedType == kind ? RelatedId : null;

    private T? PayloadValue<T>(string propertyName) where T : struct
    {
        try
        {
            using var document = JsonDocument.Parse(PayloadJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var value)
                || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return null;

            return value.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
                { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? PayloadText(string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(PayloadJson);
            return document.RootElement.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
