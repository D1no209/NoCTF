using System.Globalization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Submissions;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Management;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Submissions;

namespace NoCTF.API.Endpoints.Administration.Submissions;

public sealed class ListAdminSubmissionsRequest
{
    [QueryParam] public Guid? CompetitionChallengeId { get; set; }
    [QueryParam] public Guid? TeamId { get; set; }
    [QueryParam] public Guid? UserId { get; set; }
    [QueryParam] public SubmissionKind? SubmissionKind { get; set; }
    [QueryParam] public SubmissionEvaluationState? EvaluationState { get; set; }
    [QueryParam] public ScoringResult? ScoringResult { get; set; }
    [QueryParam] public ScoringFailureCode? FailureCode { get; set; }
    [QueryParam] public DateTimeOffset? ReceivedFrom { get; set; }
    [QueryParam] public DateTimeOffset? ReceivedTo { get; set; }
    [QueryParam] public string? SubmittedFlag { get; set; }
    [QueryParam] public bool? HasCurrentScoringEvent { get; set; }
    [QueryParam] public string? Cursor { get; set; }
    [QueryParam] public int Limit { get; set; } = 50;
}

public sealed class ListAdminSubmissionsValidator : Validator<ListAdminSubmissionsRequest>
{
    public ListAdminSubmissionsValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
}

public sealed class ListAdminSubmissionsEndpoint(
    ListSubmissions list,
    SignedKeysetCursor cursors,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ListAdminSubmissionsRequest,
        Results<Ok<SubmissionListResponse>, ForbidHttpResult, ProblemHttpResult>>
{
    private const string CursorEndpoint = "submissions.admin.list";

    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/submissions");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListSubmissions"));
        Summary(summary =>
        {
            summary.Summary = "Lists filtered competition submissions.";
            summary.Description = "Returns keyset-paged protected submission facts to authorized competition observers.";
        });
    }

    public override async Task<Results<Ok<SubmissionListResponse>, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        ListAdminSubmissionsRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var filterKey = FilterKey(request);
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, filterKey, out var position))
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid cursor.");
        var filter = new SubmissionListFilter(
            competitionId, request.CompetitionChallengeId, request.TeamId, request.UserId,
            request.SubmissionKind, request.EvaluationState, request.ScoringResult,
            request.FailureCode, request.ReceivedFrom, request.ReceivedTo,
            request.SubmittedFlag, request.HasCurrentScoringEvent);
        var items = await list.AdminAsync(
            filter, position?.CreatedAt, position?.Id, request.Limit, ct);
        var next = items.Count == request.Limit
            ? cursors.Encode(CursorEndpoint, filterKey, new(items[^1].ReceivedAt, items[^1].Id))
            : null;
        return TypedResults.Ok(new SubmissionListResponse(
            items.Select(SubmissionListMapping.ToResponse).ToArray(),
            next));
    }

    private static string FilterKey(ListAdminSubmissionsRequest request) =>
        string.Join('|',
            request.CompetitionChallengeId,
            request.TeamId,
            request.UserId,
            request.SubmissionKind,
            request.EvaluationState,
            request.ScoringResult,
            request.FailureCode,
            request.ReceivedFrom?.ToString("O", CultureInfo.InvariantCulture),
            request.ReceivedTo?.ToString("O", CultureInfo.InvariantCulture),
            request.SubmittedFlag,
            request.HasCurrentScoringEvent);
}
