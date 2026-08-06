using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Profiles;

namespace NoCTF.API.Endpoints.Teams;

public sealed class ListMyTeamsEndpoint(ListMyTeamProfiles list, IUserContext user)
    : EndpointWithoutRequest<Ok<GlobalTeamListResponse>>
{
    public override void Configure()
    {
        Get("/teams");
        AuthSchemes("Bearer");
    }

    public override async Task<Ok<GlobalTeamListResponse>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var items = await list.ExecuteAsync(user.UserId, cancellationToken);
        return TypedResults.Ok(new GlobalTeamListResponse(
            items.Select(item => GlobalTeamMapper.ToResponse(item, user.UserId)).ToArray()));
    }
}
