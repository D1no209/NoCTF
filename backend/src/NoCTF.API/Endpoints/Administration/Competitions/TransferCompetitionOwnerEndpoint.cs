using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class TransferCompetitionOwnerRequest
{
    public Guid OwnerId { get; set; }
}

public sealed class TransferCompetitionOwnerValidator : Validator<TransferCompetitionOwnerRequest>
{
    public TransferCompetitionOwnerValidator() =>
        RuleFor(request => request.OwnerId).NotEmpty();
}

public sealed class TransferCompetitionOwnerEndpoint(
    TransferCompetitionOwner transfer,
    IUserContext user)
    : Endpoint<TransferCompetitionOwnerRequest, Results<Ok<CompetitionResponse>, NotFound, Conflict>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/owner/transfer");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Transfers competition ownership.");
    }

    public override async Task<Results<Ok<CompetitionResponse>, NotFound, Conflict>> ExecuteAsync(
        TransferCompetitionOwnerRequest request,
        CancellationToken ct)
    {
        var result = await transfer.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            request.OwnerId,
            DateTimeOffset.UtcNow,
            ct);
        return result.Succeeded
            ? TypedResults.Ok(CompetitionMapper.ToResponse(result.Value!))
            : TypedResults.NotFound();
    }
}
