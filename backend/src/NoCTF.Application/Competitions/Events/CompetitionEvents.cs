using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Shared;

namespace NoCTF.Application.Competitions.Events;

public enum CompetitionEventAccessLevel : short
{
    Participant,
    Team,
    Staff
}

public sealed record CompetitionEventDraft(
    Guid CompetitionId,
    CompetitionEventKind Kind,
    CompetitionEventLevel Level,
    CompetitionEventVisibility Visibility,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId = null,
    Guid? RelatedUserId = null,
    Guid? TeamId = null,
    Guid? CompetitionChallengeId = null,
    Guid? HintId = null,
    Guid? RuntimeInstanceId = null,
    Guid? SubmissionId = null,
    Guid? ScoringEventId = null,
    Guid? QuestionId = null,
    Guid? ParentEventId = null,
    CompetitionStatus? CompetitionStatus = null,
    CompetitionLeaderboardVisibility? LeaderboardVisibility = null,
    TeamRegistrationStatus? TeamRegistrationStatus = null,
    SubmissionKind? SubmissionKind = null,
    SubmissionEvaluationState? SubmissionState = null,
    ScoringEventKind? ScoringEventKind = null,
    ScoringResult? ScoringResult = null,
    RuntimeState? RuntimeState = null,
    CompetitionQuestionStatus? QuestionStatus = null,
    int? RuntimeGeneration = null,
    int? HostPort = null,
    string? Reason = null,
    EntityReferenceKind? SubjectType = null,
    Guid? SubjectId = null,
    EntityReferenceKind? RelatedType = null,
    Guid? RelatedId = null,
    string? PayloadJson = null);

public sealed record CompetitionEventCommitted(
    Guid CompetitionId,
    Guid EventId,
    CompetitionEventKind Kind,
    CompetitionEventLevel Level,
    DateTimeOffset OccurredAt);

public interface ICompetitionEventRecorder
{
    ValueTask<Guid> RecordAsync(
        CompetitionEventDraft draft,
        CancellationToken cancellationToken = default);
}

public sealed class NullCompetitionEventRecorder : ICompetitionEventRecorder
{
    public static NullCompetitionEventRecorder Instance { get; } = new();

    private NullCompetitionEventRecorder()
    {
    }

    public ValueTask<Guid> RecordAsync(
        CompetitionEventDraft draft,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Guid.Empty);
}

public sealed record CompetitionEventQuery(
    Guid CompetitionId,
    Guid UserId,
    CompetitionEventKind? Kind,
    CompetitionEventLevel? MinimumLevel,
    Guid? TeamId,
    Guid? ActorUserId,
    Guid? CompetitionChallengeId,
    Guid? RuntimeInstanceId,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset? BeforeOccurredAt,
    Guid? BeforeId,
    int Limit);

public sealed record CompetitionEventView(
    Guid Id,
    Guid CompetitionId,
    CompetitionEventKind Kind,
    CompetitionEventLevel Level,
    CompetitionEventVisibility Visibility,
    Guid? ActorUserId,
    string? ActorDisplayName,
    Guid? RelatedUserId,
    string? RelatedUserDisplayName,
    Guid? TeamId,
    string? TeamDisplayName,
    Guid? CompetitionChallengeId,
    string? ChallengeTitle,
    Guid? HintId,
    Guid? RuntimeInstanceId,
    Guid? SubmissionId,
    Guid? ScoringEventId,
    Guid? QuestionId,
    Guid? ParentEventId,
    CompetitionStatus? CompetitionStatus,
    CompetitionLeaderboardVisibility? LeaderboardVisibility,
    TeamRegistrationStatus? TeamRegistrationStatus,
    SubmissionKind? SubmissionKind,
    SubmissionEvaluationState? SubmissionState,
    ScoringEventKind? ScoringEventKind,
    ScoringResult? ScoringResult,
    RuntimeState? RuntimeState,
    CompetitionQuestionStatus? QuestionStatus,
    int? RuntimeGeneration,
    int? HostPort,
    string? Reason,
    DateTimeOffset OccurredAt);

public enum CompetitionEventReadState : short
{
    Available,
    Forbidden,
    CompetitionNotFound,
    InvalidQuery
}

public sealed record CompetitionEventPage(
    CompetitionEventReadState State,
    CompetitionEventAccessLevel? AccessLevel = null,
    Guid? ViewerTeamId = null,
    bool CanExport = false,
    bool CanAccessSubmissionFlags = false,
    IReadOnlyList<CompetitionEventView>? Items = null);

public sealed record CompetitionEventExport(
    Stream Content,
    string FileName);

public sealed record CompetitionEventExportResult(
    CompetitionEventReadState State,
    CompetitionEventExport? Export = null);

public sealed record SubmissionFlagAccessCommand(
    Guid CompetitionId,
    Guid SubmissionId,
    Guid ActorUserId,
    string Reason,
    DateTimeOffset AccessedAt);

public sealed record SubmissionFlagAccessView(
    Guid SubmissionId,
    SubmissionKind SubmissionKind,
    string SubmittedFlag,
    DateTimeOffset AccessedAt);

public sealed record SubmissionFlagAccessResult(
    CompetitionEventReadState State,
    SubmissionFlagAccessView? View = null);

public interface ICompetitionEventStore
{
    Task<CompetitionEventPage> QueryAsync(
        CompetitionEventQuery query,
        CancellationToken cancellationToken);

    Task<CompetitionEventExportResult> ExportAsync(
        CompetitionEventQuery query,
        CancellationToken cancellationToken);

    Task<SubmissionFlagAccessResult> AccessSubmissionFlagAsync(
        SubmissionFlagAccessCommand command,
        CancellationToken cancellationToken);
}

public sealed class ListCompetitionEvents(ICompetitionEventStore store)
{
    public Task<CompetitionEventPage> ExecuteAsync(
        CompetitionEventQuery query,
        CancellationToken cancellationToken = default) =>
        IsValidRange(query, 200)
            ? store.QueryAsync(query, cancellationToken)
            : Task.FromResult(new CompetitionEventPage(CompetitionEventReadState.InvalidQuery));

    internal static bool IsValidRange(CompetitionEventQuery query, int maximumLimit) =>
        query.Limit is >= 1
        && query.Limit <= maximumLimit
        && query.From <= query.To
        && query.To - query.From <= TimeSpan.FromDays(31)
        && ((query.BeforeOccurredAt is null) == (query.BeforeId is null));
}

public sealed class ExportCompetitionEvents(ICompetitionEventStore store)
{
    public Task<CompetitionEventExportResult> ExecuteAsync(
        CompetitionEventQuery query,
        CancellationToken cancellationToken = default) =>
        ListCompetitionEvents.IsValidRange(query, 50_000)
            ? store.ExportAsync(query, cancellationToken)
            : Task.FromResult(new CompetitionEventExportResult(
                CompetitionEventReadState.InvalidQuery));
}

public sealed class AccessSubmissionFlag(ICompetitionEventStore store)
{
    public Task<SubmissionFlagAccessResult> ExecuteAsync(
        SubmissionFlagAccessCommand command,
        CancellationToken cancellationToken = default)
    {
        var reason = command.Reason.Trim();
        return reason.Length is >= 8 and <= 512
            ? store.AccessSubmissionFlagAsync(command with { Reason = reason }, cancellationToken)
            : Task.FromResult(new SubmissionFlagAccessResult(
                CompetitionEventReadState.InvalidQuery));
    }
}
