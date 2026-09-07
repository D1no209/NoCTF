using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration.PlatformConfiguration;

namespace NoCTF.API.Endpoints.Platform;

public sealed class GetPlatformLogoEndpoint(ManagePlatformConfiguration configuration)
    : EndpointWithoutRequest<Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Download)));
        Get("/platform/logo");
        AllowAnonymous();
        Description(builder => builder.WithName("PlatformLogo_Get"));
        Summary(summary =>
        {
            summary.Summary = "Returns the current platform logo.";
            summary.Description = "Streams the administrator-configured public raster logo.";
        });
    }

    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var logo = await configuration.GetLogoAsync(ct);
        if (logo is null)
            return TypedResults.NotFound();

        HttpContext.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        return TypedResults.Stream(logo.Content, logo.ContentType);
    }
}
