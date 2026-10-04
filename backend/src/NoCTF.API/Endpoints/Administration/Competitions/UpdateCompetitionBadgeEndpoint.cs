using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Application.Storage;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionBadgeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid BadgeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IFormFile? Image { get; set; }
}

public sealed class UpdateCompetitionBadgeValidator : Validator<UpdateCompetitionBadgeRequest>
{
    public UpdateCompetitionBadgeValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Description).MaximumLength(1000);
        RuleFor(request => request.Image!.Length).GreaterThan(0)
            .When(request => request.Image is not null);
        RuleFor(request => request.Image!.ContentType)
            .Must(BadgeImageValidation.IsSupported)
            .When(request => request.Image is not null);
    }
}

public sealed class UpdateCompetitionBadgeEndpoint(
    ManageCompetitionBadges badges,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    FileUploadLimits limits,
    TimeProvider clock)
    : Endpoint<UpdateCompetitionBadgeRequest,
        Results<Ok<CompetitionBadgeContract>, NotFound, ForbidHttpResult,
            ProblemHttpResult, ValidationProblem, Conflict<CompetitionBadgeConflictResponse>>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/badges/{badgeId}");
        AuthSchemes("Bearer");
        AllowFileUploads();
        MaxRequestBodySize(FileUploadLimits.MaximumRequestBytes(limits.MaximumPosterBytes));
        Description(builder => builder.WithName("AdminUpdateCompetitionBadge"));
    }

    public override async Task<Results<Ok<CompetitionBadgeContract>, NotFound, ForbidHttpResult,
        ProblemHttpResult, ValidationProblem, Conflict<CompetitionBadgeConflictResponse>>> ExecuteAsync(
        UpdateCompetitionBadgeRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        if (request.Image?.Length > limits.MaximumPosterBytes)
            return BadgeImageValidation.TooLarge(limits.MaximumPosterBytes);
        await using var stream = request.Image?.OpenReadStream();
        var result = await badges.ReplaceAsync(request.CompetitionId, request.BadgeId,
            request.Name, request.Description,
            request.Image?.FileName, request.Image?.ContentType,
            stream, clock.GetUtcNow(), ct);
        return result.Failure switch
        {
            null => TypedResults.Ok(CompetitionBadgeProtocol.ToContract(result.Badge!)),
            CompetitionBadgeFailure.NotFound => TypedResults.NotFound(),
            CompetitionBadgeFailure.ConcurrencyConflict => TypedResults.Conflict(
                new CompetitionBadgeConflictResponse(
                    CompetitionBadgeConflictCode.ConcurrencyConflict, "Badge was updated concurrently.")),
            _ => ApiProblems.ValidationProblem(new Dictionary<string, Enum?>
            {
                ["badge"] = result.Failure
            })
        };
    }
}
