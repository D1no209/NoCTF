using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class UpdateChallengeTemplateRequest
{
    public ChallengeVisibility Visibility { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Direction { get; set; } = string.Empty;
    public int ExpectedRevision { get; set; }
}

public sealed class UpdateChallengeTemplateValidator : Validator<UpdateChallengeTemplateRequest>
{
    public UpdateChallengeTemplateValidator()
    {
        RuleFor(request => request.Visibility).IsInEnum();
        RuleFor(request => request.Title).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Direction).NotEmpty().MaximumLength(96);
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateChallengeTemplateEndpoint(
    UpdateChallengeTemplate update,
    IUserContext user)
    : Endpoint<UpdateChallengeTemplateRequest, Results<Ok<ChallengeTemplateResponse>, NotFound, Conflict>>
{
    public override void Configure()
    {
        Put("/admin/challenges/{challengeId}");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Updates global challenge metadata.";
            summary.Description = "Updates the reusable template and never changes competition-owned ordering or scoring.";
        });
    }

    public override async Task<Results<Ok<ChallengeTemplateResponse>, NotFound, Conflict>> ExecuteAsync(
        UpdateChallengeTemplateRequest request,
        CancellationToken ct)
    {
        var current = await update.ExecuteAsync(new UpdateChallengeTemplateCommand(
            Route<Guid>("challengeId"),
            user.UserId,
            user.IsAdministrator,
            request.Visibility,
            request.Title,
            request.Description,
            request.Direction,
            request.ExpectedRevision,
            DateTimeOffset.UtcNow), ct);
        if (!current.Succeeded)
            return current.ErrorCode == "challenge_not_found"
                ? TypedResults.NotFound()
                : TypedResults.Conflict();
        return TypedResults.Ok(ChallengeTemplateMapper.ToResponse(current.Value!));
    }
}
