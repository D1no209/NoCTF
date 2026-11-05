using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Common;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class TestChallengeFixRequest
{
    public Guid ChallengeId { get; set; }
    public Guid RequestId { get; set; }
    public IFormFile File { get; set; } = null!;
}

public sealed record TemplateCapabilityTestAcceptedResponse(Guid ExecutionId, Guid RuntimeInstanceId, string StatusUrl);

public sealed class TestChallengeFixValidator : Validator<TestChallengeFixRequest>
{
    public TestChallengeFixValidator()
    {
        RuleFor(request => request.ChallengeId).NotEmpty();
        RuleFor(request => request.RequestId).NotEmpty();
        RuleFor(request => request.File).NotNull();
    }
}

public sealed class TestChallengeFixEndpoint(StartTemplateCapabilityTest tests, IUserContext user)
    : Endpoint<TestChallengeFixRequest, Results<Accepted<TemplateCapabilityTestAcceptedResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/fix-tests"); AllowFileUploads();
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse()));
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.TemplatePatchUpload)));
        MaxRequestBodySize(NoCTF.Application.Storage.FileUploadLimits.MaximumRequestBytes(
            NoCTF.Application.GameplayFacts.PatchUploads.PatchUploadRules.HardMaximumArchiveBytes));
        AuthSchemes("Bearer"); Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankTestFix")
            .Produces<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json"));
    }

    public override async Task<Results<Accepted<TemplateCapabilityTestAcceptedResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>>>
        ExecuteAsync(TestChallengeFixRequest request, CancellationToken ct)
    {
        await using var archive = request.File.OpenReadStream();
        return TemplateCapabilityTestAcceptanceTransport.Map(request.ChallengeId, await tests.FixAsync(
            new(request.ChallengeId, user.UserId, user.IsAdministrator), request.RequestId, false,
            request.File.FileName, request.File.ContentType, archive, ct));
    }
}

internal static class TemplateCapabilityTestAcceptanceTransport
{
    internal static Results<Accepted<TemplateCapabilityTestAcceptedResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>> Map(
        Guid challengeId, OperationResult<TemplateCapabilityTestAccepted, TemplateCapabilityTestFailure> result)
    {
        if (result.Succeeded)
        {
            var accepted = result.Value!;
            var url = $"/api/v1/admin/challenges/{challengeId}/capability-tests/{accepted.ExecutionId}";
            return TypedResults.Accepted(url, new TemplateCapabilityTestAcceptedResponse(accepted.ExecutionId, accepted.RuntimeInstanceId, url));
        }
        if (result.FailureCode == TemplateCapabilityTestFailure.NotFound) return TypedResults.NotFound();
        var error = new TemplateCapabilityTestProblem(result.FailureCode!.Value, result.ErrorMessage!);
        return result.FailureCode is TemplateCapabilityTestFailure.Conflict or TemplateCapabilityTestFailure.RequestConflict
            or TemplateCapabilityTestFailure.RuntimeNotReady ? TypedResults.Conflict(error) : TemplateCapabilityTestTransport.Invalid(error);
    }
}
