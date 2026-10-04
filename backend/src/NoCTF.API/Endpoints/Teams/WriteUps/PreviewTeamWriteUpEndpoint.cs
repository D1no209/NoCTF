using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Teams.WriteUps;
using NoCTF.Hosting.Observability;

namespace NoCTF.API.Endpoints.Teams.WriteUps;

public sealed class PreviewTeamWriteUpEndpoint(
    ManageTeamWriteUps writeUps,
    TeamWriteUpPreviewTicketCodec tickets)
    : EndpointWithoutRequest<Results<FileStreamHttpResult, NotFound,
        UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/writeup-previews/{competitionId}/{teamId}");
        AllowAnonymous();
        Options(builder => builder.WithMetadata(new ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Download)));
        Description(builder => builder
            .WithName("PreviewTeamWriteUp")
            .Produces<byte[]>(
                StatusCodes.Status200OK,
                TeamWriteUpRules.ContentType)
            .Produces(StatusCodes.Status401Unauthorized));
        Summary(summary =>
        {
            summary.Summary = "Streams a team WriteUp through a short-lived preview grant.";
            summary.Description = "The same-site HttpOnly grant is scoped to one immutable PDF. Byte ranges are enabled for progressive browser rendering.";
        });
    }

    public override async Task<Results<FileStreamHttpResult, NotFound,
        UnauthorizedHttpResult>> ExecuteAsync(CancellationToken cancellationToken)
    {
        TeamWriteUpProtocol.SetPrivateInlinePdfHeaders(HttpContext);
        var competitionId = Route<Guid>("competitionId");
        var teamId = Route<Guid>("teamId");
        if (!HttpContext.Request.Cookies.TryGetValue(
                tickets.CookieName,
                out var token)
            || !tickets.TryValidate(
                token,
                competitionId,
                teamId,
                out var fileId))
        {
            return TypedResults.Unauthorized();
        }

        var writeUp = await writeUps.OpenAsync(
            competitionId,
            teamId,
            cancellationToken);
        if (writeUp is null || writeUp.Metadata.FileId != fileId)
        {
            if (writeUp is not null)
                await writeUp.Content.DisposeAsync();
            return TypedResults.NotFound();
        }

        return TypedResults.Stream(
            writeUp.Content,
            TeamWriteUpRules.ContentType,
            enableRangeProcessing: writeUp.Content.CanSeek);
    }
}
