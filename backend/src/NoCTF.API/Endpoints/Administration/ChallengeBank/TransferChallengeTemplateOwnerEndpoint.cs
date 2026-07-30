using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class TransferChallengeTemplateOwnerRequest
{
    public Guid OwnerId { get; set; }
    public int ExpectedRevision { get; set; }
}

public sealed class TransferChallengeTemplateOwnerValidator
    : Validator<TransferChallengeTemplateOwnerRequest>
{
    public TransferChallengeTemplateOwnerValidator()
    {
        RuleFor(request => request.OwnerId).NotEmpty();
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
    }
}

public sealed class TransferChallengeTemplateOwnerEndpoint(
    TransferChallengeTemplateOwner transfer,
    IUserContext user)
    : Endpoint<TransferChallengeTemplateOwnerRequest,
        Results<
            Ok<ChallengeTemplateResponse>,
            NotFound,
            Conflict<ChallengeTemplateConflictResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/owner/transfer");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankTransferOwner"));
        Summary(summary =>
        {
            summary.Summary = "Transfers challenge template ownership.";
            summary.Description = "Assigns an eligible new owner while preserving the previous owner as a manager.";
        });
    }

    public override async Task<
        Results<
            Ok<ChallengeTemplateResponse>,
            NotFound,
            Conflict<ChallengeTemplateConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
        TransferChallengeTemplateOwnerRequest request,
        CancellationToken ct)
    {
        var result = await transfer.ExecuteAsync(
            Route<Guid>("challengeId"),
            user.UserId,
            user.IsAdministrator,
            request.OwnerId,
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
                title: "Challenge template ownership was not transferred.",
                detail: result.Detail),
            ChallengeTemplateWriteState.RevisionConflict
                or ChallengeTemplateWriteState.UserNotFound
                or ChallengeTemplateWriteState.RoleNotEligible =>
                TypedResults.Conflict(
                    ChallengeTemplateWriteResponseMapper.ToConflict(result)),
            _ => throw new InvalidOperationException(
                $"Unsupported challenge template owner transfer state: {result.State}.")
        };
    }
}
