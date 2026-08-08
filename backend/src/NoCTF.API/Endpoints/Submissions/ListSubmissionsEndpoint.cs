using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.Management;
using NoCTF.Domain.Submissions;

namespace NoCTF.API.Endpoints.Submissions;

public sealed class ListSubmissionsRequest
{
    [QueryParam]
    public string? Cursor { get; set; }
    [QueryParam]
    public int Limit { get; set; } = 50;
}

public sealed class ListSubmissionsValidator : Validator<ListSubmissionsRequest>
{
    public ListSubmissionsValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
}

public sealed record SubmissionListItemResponse(
    Guid Id,
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid TeamId,
    Guid SubmittedByUserId,
    SubmissionKindProtocol Kind,
    SubmissionEvaluationStateProtocol EvaluationState,
    ScoringResultProtocol? Result,
    ScoringFailureCodeProtocol? FailureCode,
    DateTimeOffset ReceivedAt,
    long ProcessingVersion);

public sealed record SubmissionListResponse(
    IReadOnlyList<SubmissionListItemResponse> Items,
    string? NextCursor);

internal static class SubmissionListMapping
{
    public static SubmissionListItemResponse ToResponse(SubmissionListItem item) =>
        new(
            item.Id, item.CompetitionId, item.CompetitionChallengeId, item.TeamId,
            item.SubmittedByUserId,
            SubmissionMapper.ToProtocol(item.Kind),
            SubmissionMapper.ToProtocol(item.EvaluationState),
            item.Result is null ? null : SubmissionMapper.ToProtocol(item.Result.Value),
            item.FailureCode is null ? null : SubmissionMapper.ToProtocol(item.FailureCode.Value),
            item.ReceivedAt,
            item.ProcessingVersion);
}

public sealed class ListSubmissionsEndpoint(
    ListSubmissions list,
    SignedKeysetCursor cursors,
    IUserContext user)
    : Endpoint<ListSubmissionsRequest, Results<Ok<SubmissionListResponse>, NotFound, ProblemHttpResult>>
{
    private const string CursorEndpoint = "submissions.player.list";

    public override void Configure()
    {
        Get("/competitions/{competitionId}/submissions");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Lists the current team's submissions.");
    }

    public override async Task<Results<Ok<SubmissionListResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        ListSubmissionsRequest request,
        CancellationToken ct)
    {
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, string.Empty, out var position))
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid cursor.");
        var items = await list.PlayerAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            position?.CreatedAt,
            position?.Id,
            request.Limit,
            ct);
        if (items is null)
            return TypedResults.NotFound();
        var next = items.Count == request.Limit
            ? cursors.Encode(CursorEndpoint, string.Empty, new(items[^1].ReceivedAt, items[^1].Id))
            : null;
        return TypedResults.Ok(new SubmissionListResponse(
            items.Select(SubmissionListMapping.ToResponse).ToArray(),
            next));
    }
}
