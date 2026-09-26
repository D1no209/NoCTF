using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Application.Storage;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class CreateCompetitionBadgeRequest
{
    public Guid CompetitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IFormFile Image { get; set; } = null!;
}

public sealed class CreateCompetitionBadgeValidator : Validator<CreateCompetitionBadgeRequest>
{
    public CreateCompetitionBadgeValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Description).MaximumLength(1000);
        RuleFor(request => request.Image).NotNull();
        RuleFor(request => request.Image.Length).GreaterThan(0)
            .When(request => request.Image is not null);
        RuleFor(request => request.Image.ContentType)
            .Must(BadgeImageValidation.IsSupported)
            .When(request => request.Image is not null);
    }
}

public sealed class CreateCompetitionBadgeEndpoint(
    ManageCompetitionBadges badges,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    FileUploadLimits limits,
    TimeProvider clock)
    : Endpoint<CreateCompetitionBadgeRequest,
        Results<Created<CompetitionBadgeContract>, NotFound, ForbidHttpResult,
            ProblemHttpResult, ValidationProblem>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/badges");
        AuthSchemes("Bearer");
        AllowFileUploads();
        MaxRequestBodySize(FileUploadLimits.MaximumRequestBytes(limits.MaximumPosterBytes));
        Description(builder => builder.WithName("AdminCreateCompetitionBadge"));
    }

    public override async Task<Results<Created<CompetitionBadgeContract>, NotFound, ForbidHttpResult,
        ProblemHttpResult, ValidationProblem>> ExecuteAsync(
        CreateCompetitionBadgeRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        if (request.Image.Length > limits.MaximumPosterBytes)
            return BadgeImageValidation.TooLarge(limits.MaximumPosterBytes);
        await using var stream = request.Image.OpenReadStream();
        var result = await badges.CreateAsync(request.CompetitionId,
            request.Name, request.Description,
            request.Image.FileName, request.Image.ContentType,
            stream, clock.GetUtcNow(), ct);
        if (result.Failure == CompetitionBadgeFailure.NotFound)
            return TypedResults.NotFound();
        if (result.Badge is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["badge"] = [result.Failure?.ToString() ?? "Invalid badge."]
            });
        return TypedResults.Created(
            $"/api/v1/admin/competitions/{request.CompetitionId}/badges/{result.Badge.Id}",
            CompetitionBadgeProtocol.ToContract(result.Badge));
    }
}

internal static class BadgeImageValidation
{
    public static bool IsSupported(string? contentType) =>
        contentType is "image/png" or "image/jpeg" or "image/webp";

    public static ProblemHttpResult TooLarge(long limit) => TypedResults.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "Badge image is too large.",
        detail: $"Image uploads cannot exceed {limit} bytes.");
}
