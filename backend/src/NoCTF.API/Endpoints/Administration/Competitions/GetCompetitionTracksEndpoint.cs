using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions.Tracks;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class GetCompetitionTracksEndpoint(
    GetCompetitionTracks get,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<CompetitionTrackListResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/tracks");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCompetitionTracks_Get"));
        Summary(summary =>
        {
            summary.Summary = "Gets all competition tracks.";
            summary.Description = "Includes internal tracks for authorized competition staff.";
        });
    }

    public override async Task<Results<Ok<CompetitionTrackListResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, cancellationToken))
            return TypedResults.Forbid();
        var view = await get.ExecuteAsync(
            competitionId,
            user.UserId,
            includeInternal: true,
            cancellationToken);
        return view is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(CompetitionTrackProtocolMapping.ToResponse(view));
    }
}
