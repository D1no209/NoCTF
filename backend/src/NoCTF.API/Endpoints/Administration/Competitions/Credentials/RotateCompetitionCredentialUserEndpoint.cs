using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Credentials;

namespace NoCTF.API.Endpoints.Administration.Competitions.Credentials;

public sealed record RotateCompetitionCredentialUserResponse(string Password);

public sealed class RotateCompetitionCredentialUserEndpoint(
    RotateCompetitionCredentialUser rotate,
    IUserContext user,
    TimeProvider clock)
    : EndpointWithoutRequest<Results<Ok<RotateCompetitionCredentialUserResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/credential-users/{userId}/rotate");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminRotateCompetitionCredentialUser"));
    }

    public override async Task<Results<Ok<RotateCompetitionCredentialUserResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var result = await rotate.ExecuteAsync(new(
            Route<Guid>("competitionId"), Route<Guid>("userId"), user.UserId, clock.GetUtcNow()), ct);
        return result.Failure switch
        {
            NoCTF.Application.Competitions.Credentials.CompetitionCredentialImportFailure.Forbidden => TypedResults.Forbid(),
            NoCTF.Application.Competitions.Credentials.CompetitionCredentialImportFailure.CredentialNotFound => TypedResults.NotFound(),
            _ => TypedResults.Ok(new RotateCompetitionCredentialUserResponse(result.Password!))
        };
    }
}
