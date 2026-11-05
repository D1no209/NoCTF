using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.API.Serialization;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Questionnaires.Activities;

public sealed class GetQuestionnaireReviewRequest { public Guid ActivityId { get; set; } public Guid SessionId { get; set; } }
public sealed class GetQuestionnaireReviewValidator : Validator<GetQuestionnaireReviewRequest>
{ public GetQuestionnaireReviewValidator() { RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.SessionId).NotEmpty(); } }
public sealed record QuestionnaireReviewedQuestionResponse(QuestionnaireParticipantQuestion Question, Guid RevisionId,
    IReadOnlyList<Guid> CorrectOptions, string? Explanation,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<QuestionnaireAnswerResult>))] QuestionnaireAnswerResult? Result);
public sealed record QuestionnaireReviewResponse(QuestionnaireReviewRow Session, IReadOnlyList<QuestionnaireReviewedQuestionResponse> Questions);
public sealed class GetQuestionnaireReviewEndpoint(IQuestionnaireReviewStore store, IUserContext user, TimeProvider clock)
    : Endpoint<GetQuestionnaireReviewRequest, Results<Ok<QuestionnaireReviewResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/questionnaires/activities/{activityId}/reviews/{sessionId}"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse())); Description(builder => builder.WithName("GetQuestionnaireReview"));
    }
    public override async Task<Results<Ok<QuestionnaireReviewResponse>, NotFound>> ExecuteAsync(GetQuestionnaireReviewRequest request, CancellationToken ct)
        => await store.ReadAsync(user.UserId, request.ActivityId, request.SessionId, clock.GetUtcNow(), ct) is { } result
            ? TypedResults.Ok(new QuestionnaireReviewResponse(result.Session, result.Questions.Select(question => new QuestionnaireReviewedQuestionResponse(
                question.Question, question.RevisionId, question.CorrectOptions, question.Explanation, question.Result)).ToArray()))
            : TypedResults.NotFound();
}
