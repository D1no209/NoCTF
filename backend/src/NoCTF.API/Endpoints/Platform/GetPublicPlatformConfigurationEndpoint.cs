using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.Application.Administration.PlatformConfiguration;

namespace NoCTF.API.Endpoints.Platform;

public sealed record PublicPlatformConfigurationResponse(
    string Name,
    string? Description,
    string? LogoUrl);

internal static class PublicPlatformConfigurationMapping
{
    public static PublicPlatformConfigurationResponse ToResponse(
        PlatformConfigurationView configuration,
        LinkGenerator links,
        HttpContext httpContext) =>
        new(
            configuration.Name,
            configuration.Description,
            LogoUrl(configuration, links, httpContext));

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
    LinkGenerator links)
    : EndpointWithoutRequest<Ok<PublicPlatformConfigurationResponse>>
{
    public override void Configure()
    {
        Get("/platform/configuration");
        AllowAnonymous();
        Description(builder => builder.WithName("PlatformConfiguration_Get"));
        Summary(summary =>
        {
            summary.Summary = "Returns public platform branding.";
            summary.Description = "Exposes the configured name, description, and cache-busted logo URL.";
        });
    }

    public override async Task<Ok<PublicPlatformConfigurationResponse>> ExecuteAsync(
        CancellationToken ct)
    {
        var current = await configuration.GetAsync(ct);
        return TypedResults.Ok(PublicPlatformConfigurationMapping.ToResponse(
            current,
            links,
            HttpContext));
    }
}
