using System.Globalization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;

namespace NoCTF.API.Endpoints.Competitions.Events;

public sealed class ListCompetitionEventsRequest
{
    [QueryParam] public CompetitionEventKind? Kind { get; set; }
    [QueryParam] public CompetitionEventLevel? MinimumLevel { get; set; }
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
    }
}

public sealed record CompetitionEventResponse(
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

public sealed record CompetitionEventListResponse(
    CompetitionEventAccessLevel AccessLevel,
    Guid? ViewerTeamId,
    bool CanExport,
    bool CanAccessSubmissionFlags,
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
            request.Kind,
            request.MinimumLevel,
            request.TeamId,
            request.UserId,
            request.CompetitionChallengeId,
            request.RuntimeInstanceId,
            request.From,
            request.To,
            position?.CreatedAt,
            position?.Id,
            request.Limit), cancellationToken);
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
            result.AccessLevel.Value,
            result.ViewerTeamId,
            result.CanExport,
            result.CanAccessSubmissionFlags,
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
            item.Kind,
            item.Level,
            item.Visibility,
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
            item.SubmissionId,
            item.ScoringEventId,
            item.QuestionId,
            item.CompetitionStatus,
            item.LeaderboardVisibility,
            item.TeamRegistrationStatus,
            item.SubmissionKind,
            item.SubmissionState,
            item.ScoringEventKind,
            item.ScoringResult,
            item.RuntimeState,
            item.QuestionStatus,
            item.RuntimeGeneration,
            item.HostPort,
            item.Reason,
            item.OccurredAt);
}
