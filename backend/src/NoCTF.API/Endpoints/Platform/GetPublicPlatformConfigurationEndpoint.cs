using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.Platform;

public sealed record PublicImageUploadLimitsResponse(
    long MaximumAvatarBytes,
    long MaximumWallpaperBytes);

public sealed record PublicPlatformConfigurationResponse(
    string Name,
    string? Description,
    string? LogoUrl,
    PublicImageUploadLimitsResponse ImageUploadLimits);

internal static class PublicPlatformConfigurationMapping
{
    public static PublicPlatformConfigurationResponse ToResponse(
        PlatformConfigurationView configuration,
        LinkGenerator links,
        HttpContext httpContext,
        FileUploadLimits uploadLimits) =>
        new(
            configuration.Name,
            configuration.Description,
            LogoUrl(configuration, links, httpContext),
            new(
                uploadLimits.MaximumAvatarBytes,
                uploadLimits.MaximumWallpaperBytes));

    public static string? LogoUrl(
        PlatformConfigurationView configuration,
        LinkGenerator links,
        HttpContext httpContext)
    {
        if (configuration.LogoFileId is null)
            return null;

        var path = links.GetPathByName(httpContext, "PlatformLogo_Get", values: null);
        return path is null ? null : $"{path}?v={configuration.LogoFileId.Value:N}";
    }
}

public sealed class GetPublicPlatformConfigurationEndpoint(
    ManagePlatformConfiguration configuration,
    LinkGenerator links,
    FileUploadLimits uploadLimits)
    : EndpointWithoutRequest<Ok<PublicPlatformConfigurationResponse>>
{
    public override void Configure()
    {
        Get("/platform/configuration");
        AllowAnonymous();
        Description(builder => builder.WithName("PlatformConfiguration_Get"));
        Summary(summary =>
        {
            summary.Summary = "Returns public platform branding and client capabilities.";
            summary.Description = "Exposes branding and deployment-selected image upload limits without storage metadata.";
        });
    }

    public override async Task<Ok<PublicPlatformConfigurationResponse>> ExecuteAsync(
        CancellationToken ct)
    {
        var current = await configuration.GetAsync(ct);
        return TypedResults.Ok(PublicPlatformConfigurationMapping.ToResponse(
            current,
            links,
            HttpContext,
            uploadLimits));
    }
}
