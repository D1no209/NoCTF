using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.Teams;

public sealed class ClearTeamAvatarEndpoint(
    ManageBusinessImages images,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult,
        Conflict<TeamRegistrationFailureResponse>>>
{
    public override void Configure()
    {
        Delete("/competitions/{competitionId}/teams/{teamId}/avatar");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("TeamAvatar_Clear"));
        Summary(summary => { summary.Summary = "Clears a team's avatar and queues unreferenced File cleanup."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult,
        Conflict<TeamRegistrationFailureResponse>>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await images.ClearTeamAvatarAsync(
            user.UserId,
            user.IsAdministrator,
            Route<Guid>("competitionId"),
            Route<Guid>("teamId"),
            ct);
        return result.State switch
        {
            BusinessFileReferenceState.Cleared => TypedResults.NoContent(),
            BusinessFileReferenceState.NotFound => TypedResults.NotFound(),
            BusinessFileReferenceState.Forbidden => TypedResults.Forbid(),
            BusinessFileReferenceState.Conflict => TypedResults.Conflict(
                new TeamRegistrationFailureResponse(
                    TeamMapper.ToProtocol(result.Failure!.Value),
                    "Team avatar was not cleared.")),
            _ => throw new InvalidOperationException($"Unexpected avatar state {result.State}.")
        };
    }
}
