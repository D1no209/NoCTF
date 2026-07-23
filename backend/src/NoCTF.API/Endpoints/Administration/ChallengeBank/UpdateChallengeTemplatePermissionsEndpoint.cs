using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class UpdateChallengeTemplatePermissionsRequest
{
    public Guid[] ManagerIds { get; set; } = [];
    public int ExpectedRevision { get; set; }
}

public sealed class UpdateChallengeTemplatePermissionsValidator
    : Validator<UpdateChallengeTemplatePermissionsRequest>
{
    public UpdateChallengeTemplatePermissionsValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
        RuleForEach(request => request.ManagerIds).NotEmpty();
    }
}

public sealed class UpdateChallengeTemplatePermissionsEndpoint(
    UpdateChallengeTemplatePermissions update,
    IUserContext user)
    : Endpoint<UpdateChallengeTemplatePermissionsRequest, Results<Ok<ChallengeTemplateResponse>, NotFound, Conflict>>
{
    public override void Configure()
    {
        Put("/admin/challenges/{challengeId}/permissions");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Replaces the challenge template manager set.");
    }

    public override async Task<Results<Ok<ChallengeTemplateResponse>, NotFound, Conflict>> ExecuteAsync(
        UpdateChallengeTemplatePermissionsRequest request,
        CancellationToken ct)
    {
        var result = await update.ExecuteAsync(
            Route<Guid>("challengeId"),
            user.UserId,
            user.IsAdministrator,
            request.ManagerIds,
            request.ExpectedRevision,
            DateTimeOffset.UtcNow,
            ct);
        return result.Succeeded
            ? TypedResults.Ok(ChallengeTemplateMapper.ToResponse(result.Value!))
            : TypedResults.Conflict();
    }
}
