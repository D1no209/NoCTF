using FastEndpoints;
using FluentValidation;
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
    : Endpoint<TransferChallengeTemplateOwnerRequest, Results<Ok<ChallengeTemplateResponse>, NotFound, Conflict>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/owner/transfer");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Transfers challenge template ownership.");
    }

    public override async Task<Results<Ok<ChallengeTemplateResponse>, NotFound, Conflict>> ExecuteAsync(
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
            : TypedResults.Conflict();
    }
}
