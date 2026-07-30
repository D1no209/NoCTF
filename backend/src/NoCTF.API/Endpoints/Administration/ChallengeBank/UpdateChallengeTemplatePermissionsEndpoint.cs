using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
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
    : Endpoint<UpdateChallengeTemplatePermissionsRequest,
        Results<
            Ok<ChallengeTemplateResponse>,
            NotFound,
            Conflict<ChallengeTemplateConflictResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/challenges/{challengeId}/permissions");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankUpdatePermissions"));
        Summary(summary =>
        {
            summary.Summary = "Replaces the challenge template manager set.";
            summary.Description = "Assigns the complete manager set while preserving the sole owner source of truth.";
        });
    }

    public override async Task<
        Results<
            Ok<ChallengeTemplateResponse>,
            NotFound,
            Conflict<ChallengeTemplateConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
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
        return result.State switch
        {
            ChallengeTemplateWriteState.Succeeded =>
                TypedResults.Ok(ChallengeTemplateMapper.ToResponse(result.Template!)),
            ChallengeTemplateWriteState.NotFoundOrForbidden => TypedResults.NotFound(),
            ChallengeTemplateWriteState.InvalidRequest => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Challenge template permissions were not updated.",
                detail: result.Detail),
            ChallengeTemplateWriteState.RevisionConflict
                or ChallengeTemplateWriteState.OwnerIncludedInManagerSet
                or ChallengeTemplateWriteState.UserNotFound
                or ChallengeTemplateWriteState.RoleNotEligible =>
                TypedResults.Conflict(
                    ChallengeTemplateWriteResponseMapper.ToConflict(result)),
            _ => throw new InvalidOperationException(
                $"Unsupported challenge template permission state: {result.State}.")
        };
    }
}
