using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class GetCompetitionConfigurationEndpoint(GetCompetitionConfiguration get, ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : EndpointWithoutRequest<Results<Ok<CompetitionConfigurationResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure() { Get("/admin/competitions/{competitionId}/configuration"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<CompetitionConfigurationResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var id = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, id, ct)) return TypedResults.Forbid();
        var view = await get.ExecuteAsync(id, ct);
        return view is null ? TypedResults.NotFound() : TypedResults.Ok(CompetitionConfigurationMapper.ToResponse(view));
    }
}
