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

public sealed class TestChallengeControlRequest
{
    public Guid ChallengeId { get; set; }
    public Guid RuntimeInstanceId { get; set; }
}

public sealed class TestChallengeControlValidator : Validator<TestChallengeControlRequest>
{
    public TestChallengeControlValidator()
    {
        RuleFor(request => request.ChallengeId).NotEmpty();
        RuleFor(request => request.RuntimeInstanceId).NotEmpty();
    }
}

public sealed record ChallengeControlTestResponse(Guid ExecutionId,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<ControlResult>))] ControlResult Result,
    string? ControllerIdentifier,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<CapabilityExecutionState>))] CapabilityExecutionState ExecutionState,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<CapabilityExecutionFailure>))] CapabilityExecutionFailure? ExecutionFailure,
    DateTimeOffset ObservedAt);

public sealed class TestChallengeControlEndpoint(TestChallengeCapabilities tests, IUserContext user)
    : Endpoint<TestChallengeControlRequest, Results<Ok<ChallengeControlTestResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/control-tests");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse()));
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.CapabilityTest)));
        AuthSchemes("Bearer"); Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankTestControl")
            .Produces<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json"));
    }

    public override async Task<Results<Ok<ChallengeControlTestResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>>>
        ExecuteAsync(TestChallengeControlRequest request, CancellationToken ct) =>
        TemplateCapabilityTestTransport.Map(await tests.ControlAsync(new(request.ChallengeId, user.UserId, user.IsAdministrator,
            request.RuntimeInstanceId), ct), value => new ChallengeControlTestResponse(value.ExecutionId, value.Result, value.ControllerIdentifier,
                value.ExecutionState, value.ExecutionFailure, value.ObservedAt));
}
