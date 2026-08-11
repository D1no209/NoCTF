using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Storage;
using NoCTF.Application.GameplayFacts.PatchUploads;

namespace NoCTF.API.Endpoints.Internal;

public sealed class DownloadFixArchiveEndpoint(
    IFixArchiveReader archives,
    IObjectStorage objects)
    : EndpointWithoutRequest<Results<FileStreamHttpResult, NotFound, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/api/internal/v1/awdp/fix-archives/{gameplayFactId}");
        AuthSchemes("Internal");
        Policies("FixArchiveRead");
        RoutePrefixOverride(string.Empty);
        Summary(summary =>
        {
            summary.Summary = "Download one AWDP fix archive";
            summary.Description = "Returns only the archive bound to the internal JWT gameplay-fact claim.";
        });
    }

    public override async Task<
        Results<FileStreamHttpResult, NotFound, UnauthorizedHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var routeId = Route<Guid>("gameplayFactId");
        if (!Guid.TryParse(
                User.FindFirstValue("gameplay_fact_id"),
                out var claimedId)
            || claimedId != routeId)
            return TypedResults.Unauthorized();
        var archive = await archives.FindAsync(routeId, ct);
        if (archive is null)
            return TypedResults.NotFound();
        var stream = await objects.OpenReadAsync(archive.ObjectKey, ct);
        return TypedResults.Stream(
            stream,
            string.IsNullOrWhiteSpace(archive.ContentType)
                ? "application/gzip"
                : archive.ContentType,
            archive.FileName);
    }
}
