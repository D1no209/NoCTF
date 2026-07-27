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
        Results<Ok<ChallengeTemplateResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/owner/transfer");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankTransferOwner")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Transfers challenge template ownership.";
            summary.Description = "Assigns an eligible new owner while preserving the previous owner as a manager.";
        });
    }

    public override async Task<Results<Ok<ChallengeTemplateResponse>, ProblemHttpResult>> ExecuteAsync(
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
        return result.Succeeded
            ? TypedResults.Ok(ChallengeTemplateMapper.ToResponse(result.Value!))
            : TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Challenge template ownership was not transferred.",
                detail: result.ErrorMessage);
    }
}
