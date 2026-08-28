using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http;
using NoCTF.API.Security;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UploadCompetitionPosterRequest
{
    public IFormFile File { get; set; } = null!;
}

public sealed class UploadCompetitionPosterValidator : Validator<UploadCompetitionPosterRequest>
{
    private static readonly string[] ContentTypes = ["image/jpeg", "image/png", "image/webp"];

    public UploadCompetitionPosterValidator()
    {
        RuleFor(request => request.File).NotNull();
        RuleFor(request => request.File.Length).GreaterThan(0)
            .When(request => request.File is not null);
        RuleFor(request => request.File.ContentType)
            .Must(value => ContentTypes.Contains(value, StringComparer.OrdinalIgnoreCase))
            .When(request => request.File is not null)
            .WithMessage("Poster must be a JPEG, PNG, or WebP image.");
    }
}

public sealed record CompetitionPosterResponse(Guid FileId, string ContentType);

public sealed class UploadCompetitionPosterEndpoint(
    ManageBusinessImages images,
    IUserContext user,
    FileUploadLimits uploadLimits,
    TimeProvider timeProvider)
    : Endpoint<UploadCompetitionPosterRequest,
        Results<Ok<CompetitionPosterResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/poster");
        AuthSchemes("Bearer");
        AllowFileUploads();
        MaxRequestBodySize(FileUploadLimits.MaximumRequestBytes(
            uploadLimits.MaximumPosterBytes));
        Description(builder => builder
            .WithName("AdminCompetitionPoster_Replace")
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status413PayloadTooLarge));
        Summary(summary =>
        {
            summary.Summary = "Replaces a competition poster.";
            summary.Description =
                "Stores a validated image as an immutable File and atomically replaces the competition poster reference.";
        });
    }

    public override async Task<Results<Ok<CompetitionPosterResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
        ExecuteAsync(UploadCompetitionPosterRequest request, CancellationToken ct)
    {
        if (request.File.Length > uploadLimits.MaximumPosterBytes)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Poster is too large.",
                detail: $"Poster uploads cannot exceed {uploadLimits.MaximumPosterBytes} bytes.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = FileUploadFailureCode.UploadTooLarge.ToString()
                });
        await using var content = request.File.OpenReadStream();
        var result = await images.ReplaceCompetitionPosterAsync(
            user.UserId,
            user.IsAdministrator,
            Route<Guid>("competitionId"),
            request.File.FileName,
            request.File.ContentType,
            content,
            timeProvider.GetUtcNow(),
            ct);
        return result.State switch
        {
            BusinessFileReferenceState.Updated => TypedResults.Ok(
                new CompetitionPosterResponse(result.File!.FileId, result.File.ContentType)),
            BusinessFileReferenceState.NotFound => TypedResults.NotFound(),
            BusinessFileReferenceState.Forbidden => TypedResults.Forbid(),
            _ => throw new InvalidOperationException($"Unexpected poster state {result.State}.")
        };
    }
}
