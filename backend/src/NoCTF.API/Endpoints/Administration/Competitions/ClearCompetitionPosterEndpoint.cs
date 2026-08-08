using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class ClearCompetitionPosterEndpoint(
    ManageBusinessImages images,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/poster");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCompetitionPoster_Clear"));
        Summary(summary =>
        {
            summary.Summary = "Clears a competition poster.";
            summary.Description =
                "Removes the poster reference and queues cleanup when the immutable File is no longer referenced.";
        });
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await images.ClearCompetitionPosterAsync(
            user.UserId,
            user.IsAdministrator,
            Route<Guid>("competitionId"),
            ct);
        return result.State switch
        {
            BusinessFileReferenceState.Cleared => TypedResults.NoContent(),
            BusinessFileReferenceState.NotFound => TypedResults.NotFound(),
            BusinessFileReferenceState.Forbidden => TypedResults.Forbid(),
            _ => throw new InvalidOperationException($"Unexpected poster state {result.State}.")
        };
    }
}
