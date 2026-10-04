using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Competitions.Progression;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class GetCompetitionBadgeImageRequest
{
    public Guid CompetitionId { get; set; }
    public Guid BadgeId { get; set; }
}

public sealed class GetCompetitionBadgeImageEndpoint(ManageCompetitionBadges badges)
    : Endpoint<GetCompetitionBadgeImageRequest, Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Summary(summary =>
        {
            summary.Summary = "Streams the image belonging to a visible competition badge.";
            summary.Description = summary.Summary;
        });

        Get("/competitions/{competitionId}/badges/{badgeId}/image");
        AllowAnonymous();
        Description(builder => builder.WithName("GetCompetitionBadgeImage"));
    }

    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(
        GetCompetitionBadgeImageRequest request, CancellationToken ct)
    {
        var image = await badges.OpenImageAsync(
            request.CompetitionId, request.BadgeId, ct);
        HttpContext.Response.Headers.CacheControl = "no-store";
        return image is null ? TypedResults.NotFound()
            : TypedResults.Stream(image.Content, image.ContentType, image.FileName);
    }
}
