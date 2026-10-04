using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.Application.Notifications;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class ListCompetitionAnnouncementsRequest
{
    [QueryParam]
    public string? Cursor { get; set; }

    [QueryParam]
    public int Limit { get; set; } = 50;
}

public sealed class ListCompetitionAnnouncementsValidator
    : Validator<ListCompetitionAnnouncementsRequest>
{
    public ListCompetitionAnnouncementsValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
}

public sealed record CompetitionAnnouncementResponse(
    Guid Id,
    string Title,
    string Body,
    DateTimeOffset PublishedAt);

public sealed record CompetitionAnnouncementListResponse(
    IReadOnlyList<CompetitionAnnouncementResponse> Items,
    string? NextCursor);

public sealed class ListCompetitionAnnouncementsEndpoint(
    ListPublicCompetitionAnnouncements list,
    SignedKeysetCursor cursors)
    : Endpoint<ListCompetitionAnnouncementsRequest,
        Results<Ok<CompetitionAnnouncementListResponse>, NotFound, ProblemHttpResult>>
{
    private const string CursorEndpoint = "competition.announcements.list";

    public override void Configure()
    {
        Get("/competitions/{competitionId}/announcements");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Lists public participant announcements for one competition.";
            summary.Description =
                "Returns only public announcements for a non-draft public competition.";
        });
    }

    public override async Task<
        Results<Ok<CompetitionAnnouncementListResponse>, NotFound, ProblemHttpResult>>
        ExecuteAsync(
            ListCompetitionAnnouncementsRequest request,
            CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var cursorScope = competitionId.ToString("N");
        if (!cursors.TryDecode(
                request.Cursor,
                CursorEndpoint,
                cursorScope,
                out var position))
        {
            return ApiProblems.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ApiMessages.Get(ApiMessageId.InvalidCursor));
        }

        var result = await list.ExecuteAsync(
            competitionId,
            position?.CreatedAt,
            position?.Id,
            request.Limit,
            ct);
        if (result.State == PublicCompetitionAnnouncementReadState.CompetitionNotFound
            || result.Items is null)
        {
            return TypedResults.NotFound();
        }

        var items = result.Items.Select(item => new CompetitionAnnouncementResponse(
            item.Id,
            item.Title,
            item.Body,
            item.PublishedAt)).ToArray();
        var next = items.Length == request.Limit
            ? cursors.Encode(
                CursorEndpoint,
                cursorScope,
                new(items[^1].PublishedAt, items[^1].Id))
            : null;
        return TypedResults.Ok(new CompetitionAnnouncementListResponse(items, next));
    }
}
