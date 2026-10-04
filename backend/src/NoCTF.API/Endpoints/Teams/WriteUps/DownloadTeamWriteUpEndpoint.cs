using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.WriteUps;
using NoCTF.Hosting.Observability;

namespace NoCTF.API.Endpoints.Teams.WriteUps;

public sealed class DownloadTeamWriteUpEndpoint(
    ManageTeamWriteUps writeUps,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<FileStreamHttpResult, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/teams/{teamId}/writeup/content");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Download)));
        Description(builder => builder
            .WithName("DownloadTeamWriteUp")
            .Produces<byte[]>(StatusCodes.Status200OK, "application/pdf"));
        Summary(summary => { summary.Summary = "Downloads a team's PDF WriteUp for staff review."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<FileStreamHttpResult, NotFound, ForbidHttpResult>>
        ExecuteAsync(CancellationToken cancellationToken)
    {
        TeamWriteUpProtocol.SetPrivatePdfHeaders(HttpContext);
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(
                user.UserId,
                competitionId,
                cancellationToken))
        {
            return TypedResults.Forbid();
        }
        var result = await writeUps.OpenAsync(
            competitionId,
            Route<Guid>("teamId"),
            cancellationToken);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(
                result.Content,
                TeamWriteUpRules.ContentType,
                TeamWriteUpProtocol.SafeFileName(
                    result.Metadata.FileName,
                    result.Metadata.TeamId),
                enableRangeProcessing: false);
    }
}
