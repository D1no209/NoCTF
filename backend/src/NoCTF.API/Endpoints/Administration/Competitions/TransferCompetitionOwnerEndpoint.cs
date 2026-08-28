using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
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
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<
        TransferCompetitionOwnerRequest,
        Results<
            Ok<CompetitionResponse>,
            NotFound,
            Conflict<CompetitionResourceManagerConflictResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/owner/transfer");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminTransferCompetitionOwner"));
        Summary(summary =>
        {
            summary.Summary = "Transfers competition ownership.";
            summary.Description = "Assigns a new eligible owner and moves the previous owner into the manager set.";
        });
    }

    public override async Task<
        Results<
            Ok<CompetitionResponse>,
            NotFound,
            Conflict<CompetitionResourceManagerConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
        TransferCompetitionOwnerRequest request,
        CancellationToken ct)
    {
        var result = await transfer.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            request.OwnerId,
            timeProvider.GetUtcNow(),
            ct);
        return result.State switch
        {
            CompetitionOwnerTransferState.Transferred =>
                TypedResults.Ok(CompetitionMapper.ToResponse(
                    result.Competition!,
                    timeProvider.GetUtcNow())),
            CompetitionOwnerTransferState.NotFound => TypedResults.NotFound(),
            CompetitionOwnerTransferState.UserNotFound =>
                TypedResults.Conflict(
                    CompetitionResourceManagerConflictMapper.ToResponse(
                        CompetitionResourceManagerConflictCode.UserNotFound,
                        result.UserIds)),
            CompetitionOwnerTransferState.RoleNotEligible =>
                TypedResults.Conflict(
                    CompetitionResourceManagerConflictMapper.ToResponse(
                        CompetitionResourceManagerConflictCode.RoleNotEligible,
                        result.UserIds)),
            CompetitionOwnerTransferState.InvalidOwnerId => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Competition ownership was not transferred.",
                detail: "OwnerId is required."),
            _ => throw new InvalidOperationException(
                $"Unsupported competition owner transfer state: {result.State}.")
        };
    }
}
