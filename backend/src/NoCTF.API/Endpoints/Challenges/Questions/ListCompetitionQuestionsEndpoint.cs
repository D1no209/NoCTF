using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Questions;

namespace NoCTF.API.Endpoints.Challenges.Questions;

public sealed class ListCompetitionQuestionsRequest
{
    public Guid? CompetitionChallengeId { get; set; }
    public CompetitionQuestionSubjectCode? Subject { get; set; }
    public CompetitionQuestionStatusCode? Status { get; set; }
    public string? Cursor { get; set; }
    public int Limit { get; set; } = 50;
}
public sealed class ListCompetitionQuestionsValidator
    : Validator<ListCompetitionQuestionsRequest>
{
    public ListCompetitionQuestionsValidator()
    {
        RuleFor(request => request.Subject).IsInEnum().When(request => request.Subject is not null);
        RuleFor(request => request.Status).IsInEnum().When(request => request.Status is not null);
        RuleFor(request => request.Limit)
            .InclusiveBetween(1, CompetitionQuestionRules.MaximumListLimit);
    }
}

public sealed record CompetitionQuestionListResponse(
    IReadOnlyList<CompetitionQuestionResponse> Items,
    string? NextCursor);

public sealed class ListCompetitionQuestionsEndpoint(
    ListCompetitionQuestions list,
    SignedKeysetCursor cursors,
    IUserContext user)
    : Endpoint<ListCompetitionQuestionsRequest, Results<
        Ok<CompetitionQuestionListResponse>,
        ForbidHttpResult,
        ProblemHttpResult>>
{
    private const string CursorEndpoint = "competition.questions.list";

    public override void Configure()
    {
        Get("/competitions/{competitionId}/questions");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("ListCompetitionQuestions"));
        Summary(summary =>
        {
            summary.Summary = "Lists competition questions visible to the current user.";
            summary.Description = "The asker receives their own threads; observers receive read-only projections and handlers receive their authorized queue.";
        });
    }

    public override async Task<Results<
        Ok<CompetitionQuestionListResponse>,
        ForbidHttpResult,
        ProblemHttpResult>>
        ExecuteAsync(ListCompetitionQuestionsRequest request, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var filterKey = FilterKey(competitionId, user.UserId, request);
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, filterKey, out var position))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid cursor.");
        }
        var page = await list.ExecuteAsync(new(
            competitionId,
            user.UserId,
            request.CompetitionChallengeId,
            request.Subject is null
                ? null
                : CompetitionQuestionResponseMapper.ToDomain(request.Subject.Value),
            request.Status is null
                ? null
                : CompetitionQuestionResponseMapper.ToDomain(request.Status.Value),
            request.Limit,
            position is null
                ? null
                : new CompetitionQuestionPagePosition(position.CreatedAt, position.Id)), ct);
        var nextCursor = page.NextPosition is null
            ? null
            : cursors.Encode(
                CursorEndpoint,
                filterKey,
                new(page.NextPosition.UpdatedAt, page.NextPosition.Id));
        return TypedResults.Ok(new CompetitionQuestionListResponse(
            page.Items.Select(CompetitionQuestionResponseMapper.ToResponse).ToArray(),
            nextCursor));
    }

    private static string FilterKey(
        Guid competitionId,
        Guid actorUserId,
        ListCompetitionQuestionsRequest request) =>
        string.Join(
            '|',
            competitionId,
            actorUserId,
            request.CompetitionChallengeId,
            request.Subject,
            request.Status);
}
