using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Common;
using System.Text.Json.Serialization;
using NoCTF.API.Serialization;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class TestChallengeBreakRequest
{
    public Guid ChallengeId { get; set; }
    public Guid? RuntimeInstanceId { get; set; }
    public string Flag { get; set; } = string.Empty;
}

public sealed class TestChallengeBreakValidator : Validator<TestChallengeBreakRequest>
{
    public TestChallengeBreakValidator()
    {
        RuleFor(request => request.ChallengeId).NotEmpty();
        RuleFor(request => request.Flag).NotEmpty().MaximumLength(4096);
    }
}

public sealed record TemplateCapabilityTestProblem(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<TemplateCapabilityTestFailure>))] TemplateCapabilityTestFailure Code, string Detail)
{
    public string MessageKey => $"administration.capabilityTests.error.{Code}";
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed record ChallengeBreakTestResponse(Guid ExecutionId,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<BreakMatchResult>))] BreakMatchResult Match,
    IReadOnlyList<Guid> MatchedFlagIds, DateTimeOffset ObservedAt);

public sealed class TestChallengeBreakEndpoint(TestChallengeCapabilities tests, IUserContext user)
    : Endpoint<TestChallengeBreakRequest, Results<Ok<ChallengeBreakTestResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/break-tests");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse()));
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.CapabilityTest)));
        AuthSchemes("Bearer"); Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankTestBreak")
            .Produces<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json"));
    }

    public override async Task<Results<Ok<ChallengeBreakTestResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>>>
        ExecuteAsync(TestChallengeBreakRequest request, CancellationToken ct) =>
        TemplateCapabilityTestTransport.Map(await tests.BreakAsync(new(request.ChallengeId, user.UserId, user.IsAdministrator,
            request.RuntimeInstanceId), request.Flag, ct), value => new ChallengeBreakTestResponse(value.ExecutionId, value.Match,
                value.Flags.Select(flag => flag.FlagId).ToArray(), value.ObservedAt));
}

internal static class TemplateCapabilityTestTransport
{
    internal static Results<Ok<TResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>> Map<T, TResponse>(
        OperationResult<T, TemplateCapabilityTestFailure> result, Func<T, TResponse> map)
    {
        if (result.Succeeded) return TypedResults.Ok(map(result.Value!));
        if (result.FailureCode == TemplateCapabilityTestFailure.NotFound) return TypedResults.NotFound();
        var error = new TemplateCapabilityTestProblem(result.FailureCode!.Value, result.ErrorMessage!);
        return result.FailureCode is TemplateCapabilityTestFailure.Conflict or TemplateCapabilityTestFailure.RequestConflict
            or TemplateCapabilityTestFailure.RuntimeNotReady
            ? TypedResults.Conflict(error) : Invalid(error);
    }

    internal static ProblemHttpResult Invalid(TemplateCapabilityTestProblem error) => TypedResults.Problem(
        detail: error.Detail, statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?>
        {
            ["code"] = error.Code.ToString(), ["messageKey"] = error.MessageKey, ["messageArguments"] = error.MessageArguments
        });
}
