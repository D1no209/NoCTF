using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.WriteUps;
using NoCTF.Hosting.Observability;

namespace NoCTF.API.Endpoints.Teams.WriteUps;


public sealed class DownloadMyTeamWriteUpEndpoint(
    ManageTeamWriteUps writeUps,
    IUserContext user)
    : EndpointWithoutRequest<Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/teams/me/writeup/content");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Download)));
        Description(builder => builder
            .WithName("DownloadMyTeamWriteUp")
            .Produces<byte[]>(StatusCodes.Status200OK, "application/pdf"));
        Summary(summary => { summary.Summary = "Downloads the current team's PDF WriteUp."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        TeamWriteUpProtocol.SetPrivatePdfHeaders(HttpContext);
        var result = await writeUps.OpenMineAsync(
            Route<Guid>("competitionId"),
            user.UserId,
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
