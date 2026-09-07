using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class GetCompetitionPosterEndpoint(ManageBusinessImages images)
    : EndpointWithoutRequest<Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Download)));
        Get("/competitions/{competitionId}/poster");
        AllowAnonymous();
        Description(builder => builder.WithName("CompetitionPoster_Get"));
        Summary(summary => summary.Summary = "Returns a competition's current poster.");
    }

    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var file = await images.GetCompetitionPosterAsync(Route<Guid>("competitionId"), ct);
        return file is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(file.Content, file.ContentType, file.FileName);
    }
}
