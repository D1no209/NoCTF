using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Testing;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class TestChallengeFixAndCheckRequest
{
    public Guid ChallengeId { get; set; }
    public Guid RequestId { get; set; }
    public IFormFile File { get; set; } = null!;
}

public sealed class TestChallengeFixAndCheckValidator : Validator<TestChallengeFixAndCheckRequest>
{
    public TestChallengeFixAndCheckValidator()
    {
        RuleFor(request => request.ChallengeId).NotEmpty();
        RuleFor(request => request.RequestId).NotEmpty();
        RuleFor(request => request.File).NotNull();
    }
}

public sealed class TestChallengeFixAndCheckEndpoint(StartTemplateCapabilityTest tests, IUserContext user)
    : Endpoint<TestChallengeFixAndCheckRequest, Results<Accepted<TemplateCapabilityTestAcceptedResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/fix-and-check-tests"); AllowFileUploads();
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse()));
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.TemplatePatchUpload)));
        MaxRequestBodySize(NoCTF.Application.Storage.FileUploadLimits.MaximumRequestBytes(
            NoCTF.Application.GameplayFacts.PatchUploads.PatchUploadRules.HardMaximumArchiveBytes));
        AuthSchemes("Bearer"); Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankTestFixAndCheck")
            .Produces<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json"));
    }

    public override async Task<Results<Accepted<TemplateCapabilityTestAcceptedResponse>, NotFound, ProblemHttpResult, Conflict<TemplateCapabilityTestProblem>>>
        ExecuteAsync(TestChallengeFixAndCheckRequest request, CancellationToken ct)
    {
        await using var archive = request.File.OpenReadStream();
        return TemplateCapabilityTestAcceptanceTransport.Map(request.ChallengeId, await tests.FixAsync(
            new(request.ChallengeId, user.UserId, user.IsAdministrator), request.RequestId, true,
            request.File.FileName, request.File.ContentType, archive, ct));
    }
}
