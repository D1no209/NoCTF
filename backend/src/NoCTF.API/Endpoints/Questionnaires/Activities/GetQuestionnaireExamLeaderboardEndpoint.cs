using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Activities;

namespace NoCTF.API.Endpoints.Questionnaires.Activities;

public sealed class GetQuestionnaireExamLeaderboardRequest : NoCTF.API.Pagination.PaginationRequest { public Guid ActivityId { get; set; } }
public sealed class GetQuestionnaireExamLeaderboardValidator : Validator<GetQuestionnaireExamLeaderboardRequest>
{ public GetQuestionnaireExamLeaderboardValidator() { NoCTF.API.Pagination.PaginationRules.Add(this); RuleFor(request => request.ActivityId).NotEmpty(); } }
public sealed class GetQuestionnaireExamLeaderboardEndpoint(IQuestionnaireReviewStore store, IUserContext user, TimeProvider clock)
    : Endpoint<GetQuestionnaireExamLeaderboardRequest, Results<Ok<QuestionnaireExamLeaderboard>, NotFound>>
{
    public override void Configure()
    {
        Get("/questionnaires/activities/{activityId}/leaderboard"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse())); Description(builder => builder.WithName("GetQuestionnaireExamLeaderboard"));
    }
    public override async Task<Results<Ok<QuestionnaireExamLeaderboard>, NotFound>> ExecuteAsync(GetQuestionnaireExamLeaderboardRequest request, CancellationToken ct)
        => await store.LeaderboardAsync(user.UserId, request.ActivityId, request.Offset, request.Limit, clock.GetUtcNow(), ct) is { } result ? TypedResults.Ok(result) : TypedResults.NotFound();
}
