using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.Teams;

public sealed class GetTeamAvatarEndpoint(ManageBusinessImages images)
    : EndpointWithoutRequest<Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/teams/{teamId}/avatar");
        AllowAnonymous();
        Description(builder => builder.WithName("TeamAvatar_Get"));
        Summary(summary => summary.Summary = "Returns a team's current avatar.");
    }

    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var file = await images.GetTeamAvatarAsync(
            Route<Guid>("competitionId"), Route<Guid>("teamId"), ct);
        return file is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(file.Content, file.ContentType, file.FileName);
    }
}
