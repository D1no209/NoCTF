using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Challenges.Testing;
using System.Text.Json.Serialization;
using NoCTF.API.Serialization;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class TestChallengeQuestionnaireRequest
{
    public Guid ChallengeId { get; set; }
    public List<QuestionnaireTestAnswerRequest> Answers { get; set; } = [];
}

public sealed record QuestionnaireTestAnswerRequest(Guid QuestionId, IReadOnlyList<Guid> SelectedOptionIds);
public sealed record QuestionnaireTestQuestionResponse(Guid QuestionId,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<QuestionnaireAnswerResult>))] QuestionnaireAnswerResult Result);
public sealed record ChallengeQuestionnaireTestResponse(Guid ExecutionId, Guid RevisionId,
    IReadOnlyList<QuestionnaireTestQuestionResponse> Questions, DateTimeOffset ObservedAt);

public sealed class TestChallengeQuestionnaireValidator : Validator<TestChallengeQuestionnaireRequest>
{
    public TestChallengeQuestionnaireValidator()
    {
        RuleFor(request => request.ChallengeId).NotEmpty();
        RuleFor(request => request.Answers).NotNull().Must(answers => answers.Count <= 1000);
        RuleForEach(request => request.Answers).NotNull().ChildRules(answer =>
        {
            answer.RuleFor(value => value.QuestionId).NotEmpty();
            answer.RuleFor(value => value.SelectedOptionIds).NotNull().Must(options => options.Count <= 8);
        });
    }
}

public sealed class TestChallengeQuestionnaireEndpoint(TestChallengeCapabilities tests, IUserContext user)
    : Endpoint<TestChallengeQuestionnaireRequest, Results<Ok<ChallengeQuestionnaireTestResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/questionnaire-tests");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse()));
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.CapabilityTest)));
        AuthSchemes("Bearer"); Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankTestQuestionnaire")
            .Produces<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json"));
    }

    public override async Task<Results<Ok<ChallengeQuestionnaireTestResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>>>
        ExecuteAsync(TestChallengeQuestionnaireRequest request, CancellationToken ct) =>
        TemplateCapabilityTestTransport.Map(await tests.QuestionnaireAsync(new(request.ChallengeId, user.UserId, user.IsAdministrator),
            request.Answers.Select(answer => new QuestionnaireAnswer(answer.QuestionId, answer.SelectedOptionIds)).ToArray(), ct),
            value => new ChallengeQuestionnaireTestResponse(value.ExecutionId, value.RevisionId,
                value.Questions.Select(question => new QuestionnaireTestQuestionResponse(question.QuestionId, question.Result)).ToArray(), value.ObservedAt));
}
