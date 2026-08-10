using System.Globalization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Endpoints.Teams;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Competitions.Events;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionEventKindProtocol>))]
public enum CompetitionEventKindProtocol
{
    CompetitionCreated, CompetitionUpdated, CompetitionDeleted, CompetitionLifecycleChanged,
    LeaderboardVisibilityChanged, ChallengeCreated, ChallengeUpdated, ChallengePublished,
    ChallengeUnpublished, ChallengeDeleted, HintPublished, HintUnlocked, TeamRegistered,
    TeamRegistrationChanged, TeamUpdated, TeamDeleted, TeamMemberJoined, TeamMemberRemoved,
    TeamCaptainTransferred, TeamBanned, TeamUnbanned, GameplayFactReceived, GameplayFactAdjudicated,
    ScoringRecorded, FirstBloodAwarded, SecondBloodAwarded, ThirdBloodAwarded, RuntimeCreated,
    RuntimeStateChanged, RuntimeExtended, RuntimeReset, RuntimePortAllocated,
    ProtectedGameplayFactValueAccessed, CheatIncidentDetected,
    CheatIncidentConfirmed, CheatIncidentDismissed, CheatIncidentSuperseded,
    CheatIncidentCorrected, ProtectedCompetitionExportCreated, TeamBanAppealSubmitted,
    TeamBanAppealUpheld, TeamBanAppealAccepted, TeamBanCorrectionPublished,
    RuntimeForceTerminationRequested, RuntimeForceTerminationCompleted,
    RuntimeForceTerminationFailed, AnnouncementPublished, QuestionOpened,
    QuestionReplied, QuestionStatusChanged, ChallengeDescriptionUpdated
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionEventLevelProtocol>))]
public enum CompetitionEventLevelProtocol { Information, Warning, Error }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionEventVisibilityProtocol>))]
public enum CompetitionEventVisibilityProtocol { Public, Team, Staff }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionEventAccessLevelProtocol>))]
public enum CompetitionEventAccessLevelProtocol { Participant, Team, Staff }


[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionQuestionStatusProtocol>))]
public enum CompetitionQuestionStatusProtocol { Pending, Replied, Resolved, Closed }

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeCleanupResultProtocol>))]
public enum RuntimeCleanupResultProtocol
{
    Pending,
    ResourcesAbsent,
    ResourcesRemain,
    CleanupFailed,
    CapacityOwnershipConflict
}

[Mapper]
internal static partial class CompetitionEventProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)] public static partial CompetitionEventKindProtocol ToProtocol(CompetitionEventKind value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial CompetitionEventLevelProtocol ToProtocol(CompetitionEventLevel value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial CompetitionEventVisibilityProtocol ToProtocol(CompetitionEventVisibility value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial CompetitionEventAccessLevelProtocol ToProtocol(CompetitionEventAccessLevel value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial CompetitionQuestionStatusProtocol ToProtocol(CompetitionQuestionStatus value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial RuntimeCleanupResultProtocol ToProtocol(RuntimeCleanupResult value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial CompetitionEventKind ToDomain(CompetitionEventKindProtocol value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial CompetitionEventLevel ToDomain(CompetitionEventLevelProtocol value);
}

public sealed class ListCompetitionEventsRequest
{
    [QueryParam] public CompetitionEventKindProtocol? Kind { get; set; }
    [QueryParam] public CompetitionEventKindProtocol[]? Kinds { get; set; }
    [QueryParam] public CompetitionEventLevelProtocol? MinimumLevel { get; set; }
    [QueryParam] public Guid? TeamId { get; set; }
    [QueryParam] public Guid? UserId { get; set; }
    [QueryParam] public Guid? CompetitionChallengeId { get; set; }
    [QueryParam] public Guid? RuntimeInstanceId { get; set; }
    [QueryParam] public DateTimeOffset From { get; set; }
    [QueryParam] public DateTimeOffset To { get; set; }
    [QueryParam] public string? Cursor { get; set; }
    [QueryParam] public int Limit { get; set; } = 50;
}

public sealed class ListCompetitionEventsValidator
    : Validator<ListCompetitionEventsRequest>
{
    public ListCompetitionEventsValidator()
    {
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
        RuleFor(request => request.From).NotEmpty();
        RuleFor(request => request.To).NotEmpty();
        RuleFor(request => request).Must(request =>
                request.From <= request.To
                && request.To - request.From <= TimeSpan.FromDays(31))
            .WithMessage("The event query range must be between zero and 31 days.");
        RuleFor(request => request).Must(request =>
                request.Kind is null || request.Kinds is null or { Length: 0 })
            .WithMessage("Specify either kind or kinds, not both.");
    }
}

public sealed record CompetitionEventResponse(
    Guid Id,
    Guid CompetitionId,
    CompetitionEventKindProtocol Kind,
    CompetitionEventLevelProtocol Level,
    CompetitionEventVisibilityProtocol Visibility,
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
    CompetitionStatusProtocol? CompetitionStatus,
    LeaderboardVisibilityProtocol? LeaderboardVisibility,
    TeamRegistrationStatusProtocol? TeamRegistrationStatus,
    GameplayFactKindProtocol? GameplayFactKind,
    GameplayFactStateProtocol? GameplayFactState,
    GameplayFactResultProtocol? GameplayFactResult,
    RuntimeStateProtocol? RuntimeState,
    RuntimeCleanupResultProtocol? RuntimeCleanupResult,
    CompetitionQuestionStatusProtocol? QuestionStatus,
    int? RuntimeGeneration,
    int? HostPort,
    string? Reason,
    DateTimeOffset OccurredAt);

public sealed record CompetitionEventListResponse(
    CompetitionEventAccessLevelProtocol AccessLevel,
    Guid? ViewerTeamId,
    bool CanExport,
    bool CanAccessGameplayFactValues,
    IReadOnlyList<CompetitionEventResponse> Items,
    string? NextCursor);

public sealed class ListCompetitionEventsEndpoint(
    ListCompetitionEvents list,
    SignedKeysetCursor cursors,
    IUserContext user)
    : Endpoint<ListCompetitionEventsRequest,
        Results<Ok<CompetitionEventListResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    private const string CursorEndpoint = "competition.events.list";

    public override void Configure()
    {
        Get("/competitions/{competitionId}/events");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("ListCompetitionEvents"));
        Summary(summary =>
        {
            summary.Summary = "Lists one competition's immutable event feed.";
            summary.Description =
                "Applies participant, team, and staff visibility before returning a signed keyset page.";
        });
    }

    public override async Task<
        Results<Ok<CompetitionEventListResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
        ExecuteAsync(
            ListCompetitionEventsRequest request,
            CancellationToken cancellationToken)
    {
        var competitionId = Route<Guid>("competitionId");
        var filterKey = FilterKey(competitionId, request);
        if (!cursors.TryDecode(
                request.Cursor,
                CursorEndpoint,
                filterKey,
                out var position))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid cursor.");
        }

        var result = await list.ExecuteAsync(new CompetitionEventQuery(
            competitionId,
            user.UserId,
            request.Kind is null ? null : CompetitionEventProtocolMapper.ToDomain(request.Kind.Value),
            request.MinimumLevel is null ? null : CompetitionEventProtocolMapper.ToDomain(request.MinimumLevel.Value),
            request.TeamId,
            request.UserId,
            request.CompetitionChallengeId,
            request.RuntimeInstanceId,
            request.From,
            request.To,
            position?.CreatedAt,
            position?.Id,
            request.Limit,
            request.Kinds?.Select(CompetitionEventProtocolMapper.ToDomain).ToArray()), cancellationToken);
        if (result.State == CompetitionEventReadState.Forbidden)
            return TypedResults.Forbid();
        if (result.State == CompetitionEventReadState.CompetitionNotFound)
            return TypedResults.NotFound();
        if (result.State != CompetitionEventReadState.Available
            || result.AccessLevel is null
            || result.Items is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid competition event query.");
        }

        var nextCursor = result.Items.Count == request.Limit
            ? cursors.Encode(
                CursorEndpoint,
                filterKey,
                new(
                    result.Items[^1].OccurredAt,
                    result.Items[^1].Id))
            : null;
        return TypedResults.Ok(new CompetitionEventListResponse(
            CompetitionEventProtocolMapper.ToProtocol(result.AccessLevel.Value),
            result.ViewerTeamId,
            result.CanExport,
            result.CanAccessGameplayFactValues,
            result.Items.Select(Map).ToArray(),
            nextCursor));
    }

    internal static string FilterKey(
        Guid competitionId,
        ListCompetitionEventsRequest request) =>
        string.Join(
            '|',
            competitionId,
            request.Kind,
            string.Join(',', (request.Kinds ?? []).OrderBy(item => item)),
            request.MinimumLevel,
            request.TeamId,
            request.UserId,
            request.CompetitionChallengeId,
            request.RuntimeInstanceId,
            request.From.ToString("O", CultureInfo.InvariantCulture),
            request.To.ToString("O", CultureInfo.InvariantCulture));

    internal static CompetitionEventResponse Map(CompetitionEventView item) =>
        new(
            item.Id,
            item.CompetitionId,
            CompetitionEventProtocolMapper.ToProtocol(item.Kind),
            CompetitionEventProtocolMapper.ToProtocol(item.Level),
            CompetitionEventProtocolMapper.ToProtocol(item.Visibility),
            item.ActorUserId,
            item.ActorDisplayName,
            item.RelatedUserId,
            item.RelatedUserDisplayName,
            item.TeamId,
            item.TeamDisplayName,
            item.CompetitionChallengeId,
            item.ChallengeTitle,
            item.HintId,
            item.RuntimeInstanceId,
            item.GameplayFactId,
            item.QuestionId,
            item.ParentEventId,
            item.CompetitionStatus is null ? null : CompetitionProtocolMapper.ToProtocol(item.CompetitionStatus.Value),
            item.LeaderboardVisibility is null ? null : CompetitionProtocolMapper.ToProtocol(item.LeaderboardVisibility.Value),
            item.TeamRegistrationStatus is null ? null : TeamMapper.ToProtocol(item.TeamRegistrationStatus.Value),
            item.GameplayFactKind is null ? null : GameplayFactMapper.ToProtocol(item.GameplayFactKind.Value),
            item.GameplayFactState is null ? null : GameplayFactMapper.ToProtocol(item.GameplayFactState.Value),
            item.GameplayFactResult is null ? null : GameplayFactMapper.ToProtocol(item.GameplayFactResult.Value),
            item.RuntimeState is null ? null : RuntimeProtocolMapper.ToProtocol(item.RuntimeState.Value),
            item.RuntimeCleanupResult is null
                ? null
                : CompetitionEventProtocolMapper.ToProtocol(item.RuntimeCleanupResult.Value),
            item.QuestionStatus is null ? null : CompetitionEventProtocolMapper.ToProtocol(item.QuestionStatus.Value),
            item.RuntimeGeneration,
            item.HostPort,
            item.Reason,
            item.OccurredAt);
}
