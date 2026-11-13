using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Credentials;

namespace NoCTF.API.Endpoints.Administration.Competitions.Credentials;

public sealed class RotateCompetitionCredentialBatchEndpoint(
    RotateCompetitionCredentialBatch rotate,
    IUserContext user,
    TimeProvider clock)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/credential-batches/{batchId}/rotate");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminRotateCompetitionCredentialBatch"));
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var result = await rotate.ExecuteAsync(new(
            Route<Guid>("competitionId"), Route<Guid>("batchId"), user.UserId, clock.GetUtcNow()), ct);
        return result.Failure switch
        {
            NoCTF.Application.Competitions.Credentials.CompetitionCredentialImportFailure.Forbidden => TypedResults.Forbid(),
            NoCTF.Application.Competitions.Credentials.CompetitionCredentialImportFailure.CredentialNotFound => TypedResults.NotFound(),
            _ => TypedResults.NoContent()
        };
    }
}
