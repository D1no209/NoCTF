using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class RestoreCompetitionEndpoint(
    RestoreCompetition restore,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/restore");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Restores a soft-deleted competition.");
    }

    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var result = await restore.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            DateTimeOffset.UtcNow,
            ct);
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
