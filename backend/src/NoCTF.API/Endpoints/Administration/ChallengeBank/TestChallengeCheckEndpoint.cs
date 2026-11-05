using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Testing;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class TestChallengeCheckRequest
{
    public Guid ChallengeId { get; set; }
    public Guid RequestId { get; set; }
    public Guid RuntimeInstanceId { get; set; }
}

public sealed class TestChallengeCheckValidator : Validator<TestChallengeCheckRequest>
{
    public TestChallengeCheckValidator()
    {
        RuleFor(request => request.ChallengeId).NotEmpty();
        RuleFor(request => request.RequestId).NotEmpty();
        RuleFor(request => request.RuntimeInstanceId).NotEmpty();
    }
}

public sealed class TestChallengeCheckEndpoint(StartTemplateCapabilityTest tests, IUserContext user)
    : Endpoint<TestChallengeCheckRequest, Results<Accepted<TemplateCapabilityTestAcceptedResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/check-tests");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse()));
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.CapabilityTest)));
        AuthSchemes("Bearer"); Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankTestCheck")
            .Produces<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json"));
    }

    public override async Task<Results<Accepted<TemplateCapabilityTestAcceptedResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>>>
        ExecuteAsync(TestChallengeCheckRequest request, CancellationToken ct) =>
        TemplateCapabilityTestAcceptanceTransport.Map(request.ChallengeId, await tests.CheckAsync(new(request.ChallengeId,
            user.UserId, user.IsAdministrator, request.RuntimeInstanceId), request.RequestId, ct));
}
