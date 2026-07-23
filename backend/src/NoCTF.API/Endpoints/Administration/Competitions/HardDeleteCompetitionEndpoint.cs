using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class HardDeleteCompetitionEndpoint(
    HardDeleteCompetition hardDelete,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, Conflict>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/hard-delete");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Permanently deletes an empty soft-deleted competition.";
            summary.Description = "Restrict foreign keys prevent deleting a competition that owns durable facts.";
        });
    }

    public override async Task<Results<NoContent, NotFound, Conflict>> ExecuteAsync(CancellationToken ct)
    {
        var result = await hardDelete.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            ct);
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.Conflict();
    }
}
