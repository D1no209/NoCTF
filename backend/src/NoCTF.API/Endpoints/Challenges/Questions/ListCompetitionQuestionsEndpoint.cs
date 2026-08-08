using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Questions;

namespace NoCTF.API.Endpoints.Challenges.Questions;

public sealed class ListCompetitionQuestionsRequest
{
    public Guid? CompetitionChallengeId { get; set; }
    public CompetitionQuestionSubjectCode? Subject { get; set; }
    public CompetitionQuestionStatusCode? Status { get; set; }
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
    IReadOnlyList<CompetitionQuestionResponse> Items);

public sealed class ListCompetitionQuestionsEndpoint(
    ListCompetitionQuestions list,
    IUserContext user)
    : Endpoint<ListCompetitionQuestionsRequest, Results<Ok<CompetitionQuestionListResponse>, ForbidHttpResult>>
{
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

    public override async Task<Results<Ok<CompetitionQuestionListResponse>, ForbidHttpResult>>
        ExecuteAsync(ListCompetitionQuestionsRequest request, CancellationToken ct)
    {
        if (!user.IsHuman)
            return TypedResults.Forbid();
        var items = await list.ExecuteAsync(new(
            Route<Guid>("competitionId"),
            user.UserId,
            request.CompetitionChallengeId,
            request.Subject is null
                ? null
                : CompetitionQuestionResponseMapper.ToDomain(request.Subject.Value),
            request.Status is null
                ? null
                : CompetitionQuestionResponseMapper.ToDomain(request.Status.Value),
            request.Limit), ct);
        return TypedResults.Ok(new CompetitionQuestionListResponse(
            items.Select(CompetitionQuestionResponseMapper.ToResponse).ToArray()));
    }
}
