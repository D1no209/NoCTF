using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class HardDeleteCompetitionEndpoint(
    HardDeleteCompetition hardDelete,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, ProblemHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/hard-delete");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminHardDeleteCompetition")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Permanently deletes an empty soft-deleted competition.";
            summary.Description = "Restrict foreign keys prevent deleting a competition that owns durable facts.";
        });
    }

    public override async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var result = await hardDelete.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            ct);
        return result.Succeeded
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Competition was not permanently deleted.",
                detail: result.ErrorMessage);
    }
}
