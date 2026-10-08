using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.WriteUps;

namespace NoCTF.API.Endpoints.Challenges.WriteUps;

public sealed class DownloadChallengeWriteUpRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid VersionId { get; set; }
    public bool Staff { get; set; }
    public bool Download { get; set; }
}
[NJsonSchema.Annotations.JsonSchema(NJsonSchema.JsonObjectType.String, Format = "binary")]
public sealed class ChallengeWriteUpBinaryResponse;
public sealed class DownloadChallengeWriteUpEndpoint(ManageChallengeWriteUps writeUps, IUserContext user, TimeProvider clock)
    : Endpoint<DownloadChallengeWriteUpRequest, Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/writeups/versions/{versionId}/file"); AuthSchemes("Bearer");
        Options(x => x.WithMetadata(new WriteUpBrowserAccessMetadata()));
        Description(x => x.WithName("DownloadChallengeWriteUp").Produces<ChallengeWriteUpBinaryResponse>(StatusCodes.Status200OK, "application/pdf"));
        Summary(x => x.Summary = "Streams an authorized PDF and rechecks current publication, team and account access for each request.");
    }
    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(DownloadChallengeWriteUpRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        HttpContext.Response.Headers.XContentTypeOptions = "nosniff";
        var pdf = await writeUps.OpenPdfAsync(request.CompetitionId, request.CompetitionChallengeId,
            request.VersionId, user.UserId, request.Staff, clock.GetUtcNow(), ct);
        if (pdf is not null && !WriteUpBrowserAccess.MatchesFile(HttpContext, pdf.FileId))
        {
            await pdf.Content.DisposeAsync(); return TypedResults.NotFound();
        }
        return pdf is null ? TypedResults.NotFound() : TypedResults.Stream(pdf.Content, "application/pdf",
            fileDownloadName: request.Download ? pdf.FileName : null, enableRangeProcessing: pdf.Content.CanSeek);
    }
}
