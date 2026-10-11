using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Questions;

namespace NoCTF.API.Endpoints.Theory.Questions;

public sealed class UploadTheoryImageRequest { public Guid QuestionId { get; set; } public IFormFile File { get; set; } = null!; }
public sealed class UploadTheoryImageValidator : Validator<UploadTheoryImageRequest>
{
    public UploadTheoryImageValidator()
    {
        RuleFor(request => request.QuestionId).NotEmpty(); RuleFor(request => request.File).NotNull();
        When(request => request.File is not null, () => RuleFor(request => request.File.Length).InclusiveBetween(1, ManageTheoryImages.MaximumBytes));
    }
}
public sealed class UploadTheoryImageEndpoint(ManageTheoryImages images, IUserContext user, TimeProvider clock)
    : Endpoint<UploadTheoryImageRequest, Results<Ok<TheoryImageResult>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/theory/questions/{questionId}/images"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
         AllowFileUploads();
        MaxRequestBodySize(NoCTF.Application.Storage.FileUploadLimits.MaximumRequestBytes(ManageTheoryImages.MaximumBytes));
        Description(builder => builder.WithName("UploadTheoryImage"));
    }
    public override async Task<Results<Ok<TheoryImageResult>, NotFound, ProblemHttpResult>> ExecuteAsync(UploadTheoryImageRequest request, CancellationToken ct)
    {
        await using var content = request.File.OpenReadStream();
        var result = await images.UploadAsync(user.UserId, request.QuestionId, content, clock.GetUtcNow(), ct);
        return result.Failure is null ? TypedResults.Ok(result) : result.Failure == TheoryImageFailure.NotFoundOrForbidden ? TypedResults.NotFound()
            : ApiProblems.Problem(statusCode: result.Failure == TheoryImageFailure.Conflict ? 409 : 400, detail: ApiMessages.For(result.Failure),
                extensions: new Dictionary<string, object?> { ["code"] = result.Failure.ToString() });
    }
}
