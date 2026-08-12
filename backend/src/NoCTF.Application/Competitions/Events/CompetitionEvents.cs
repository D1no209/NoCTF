using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;
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
    Guid? GameplayFactId = null,
    Guid? QuestionId = null,
    Guid? ParentEventId = null,
    CompetitionStatus? CompetitionStatus = null,
    CompetitionLeaderboardVisibility? LeaderboardVisibility = null,
    TeamRegistrationStatus? TeamRegistrationStatus = null,
    GameplayFactKind? GameplayFactKind = null,
    GameplayFactState? GameplayFactState = null,
    GameplayFactResult? GameplayFactResult = null,
    RuntimeState? RuntimeState = null,
    RuntimeCleanupResult? RuntimeCleanupResult = null,
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
    DateTimeOffset? From,
    DateTimeOffset? To,
    DateTimeOffset? BeforeOccurredAt,
    Guid? BeforeId,
    int Limit,
    IReadOnlyList<CompetitionEventKind>? Kinds = null);

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
    Guid? GameplayFactId,
    Guid? QuestionId,
    Guid? ParentEventId,
    CompetitionStatus? CompetitionStatus,
    CompetitionLeaderboardVisibility? LeaderboardVisibility,
    TeamRegistrationStatus? TeamRegistrationStatus,
    GameplayFactKind? GameplayFactKind,
    GameplayFactState? GameplayFactState,
    GameplayFactResult? GameplayFactResult,
    RuntimeState? RuntimeState,
    RuntimeCleanupResult? RuntimeCleanupResult,
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
    bool CanAccessGameplayFactValues = false,
    IReadOnlyList<CompetitionEventView>? Items = null);

public sealed record CompetitionEventExport(
    Stream Content,
    string FileName);

public sealed record CompetitionEventExportResult(
    CompetitionEventReadState State,
    CompetitionEventExport? Export = null);

public sealed record GameplayFactValueAccessCommand(
    Guid CompetitionId,
    Guid GameplayFactId,
    Guid ActorUserId,
    DateTimeOffset AccessedAt);

public sealed record GameplayFactValueAccessView(
    Guid GameplayFactId,
    GameplayFactKind GameplayFactKind,
    string Value,
    DateTimeOffset AccessedAt);

public sealed record GameplayFactValueAccessResult(
    CompetitionEventReadState State,
    GameplayFactValueAccessView? View = null);

public interface ICompetitionEventStore
{
    Task<CompetitionEventPage> QueryAsync(
        CompetitionEventQuery query,
        CancellationToken cancellationToken);

    Task<CompetitionEventExportResult> ExportAsync(
        CompetitionEventQuery query,
        CancellationToken cancellationToken);

    Task<GameplayFactValueAccessResult> AccessGameplayFactValueAsync(
        GameplayFactValueAccessCommand command,
        CancellationToken cancellationToken);
}

public sealed class ListCompetitionEvents(ICompetitionEventStore store)
{
    public Task<CompetitionEventPage> ExecuteAsync(
        CompetitionEventQuery query,
        CancellationToken cancellationToken = default) =>
        IsValidRange(query, 200, allowUnbounded: true)
            ? store.QueryAsync(query, cancellationToken)
            : Task.FromResult(new CompetitionEventPage(CompetitionEventReadState.InvalidQuery));

    internal static bool IsValidRange(
        CompetitionEventQuery query,
        int maximumLimit,
        bool allowUnbounded) =>
        query.Limit is >= 1
        && query.Limit <= maximumLimit
        && (query.From is null && query.To is null
            ? allowUnbounded
            : query.From is DateTimeOffset from
              && query.To is DateTimeOffset to
              && from <= to
              && to - from <= TimeSpan.FromDays(31))
        && (query.Kind is null || query.Kinds is null or { Count: 0 })
        && ((query.BeforeOccurredAt is null) == (query.BeforeId is null));
}

public sealed class ExportCompetitionEvents(ICompetitionEventStore store)
{
    public Task<CompetitionEventExportResult> ExecuteAsync(
        CompetitionEventQuery query,
        CancellationToken cancellationToken = default) =>
        ListCompetitionEvents.IsValidRange(query, 50_000, allowUnbounded: false)
            ? store.ExportAsync(query, cancellationToken)
            : Task.FromResult(new CompetitionEventExportResult(
                CompetitionEventReadState.InvalidQuery));
}

public sealed class AccessGameplayFactValue(ICompetitionEventStore store)
{
    public Task<GameplayFactValueAccessResult> ExecuteAsync(
        GameplayFactValueAccessCommand command,
        CancellationToken cancellationToken = default) =>
        store.AccessGameplayFactValueAsync(command, cancellationToken);
}
