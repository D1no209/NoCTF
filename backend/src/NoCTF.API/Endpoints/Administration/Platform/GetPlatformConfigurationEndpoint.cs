using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Endpoints.Platform;
using NoCTF.Application.Administration.PlatformConfiguration;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record PlatformConfigurationResponse(
    string Name,
    string? Description,
    string? LogoUrl,
    long Revision,
    DateTimeOffset UpdatedAt);

internal static class PlatformConfigurationMapping
{
    public static PlatformConfigurationResponse ToResponse(
        PlatformConfigurationView configuration,
        LinkGenerator links,
        HttpContext httpContext) =>
        new(
            configuration.Name,
            configuration.Description,
            PublicPlatformConfigurationMapping.LogoUrl(configuration, links, httpContext),
            configuration.Revision,
            configuration.UpdatedAt);
}

public sealed class GetPlatformConfigurationEndpoint(
    ManagePlatformConfiguration configuration,
    LinkGenerator links)
    : EndpointWithoutRequest<Ok<PlatformConfigurationResponse>>
{
    public override void Configure()
    {
        Get("/admin/platform/configuration");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformGetConfiguration"));
        Summary(summary =>
        {
            summary.Summary = "Returns editable platform branding configuration.";
            summary.Description = "Returns public branding fields with their optimistic revision.";
        });
    }

    public override async Task<Ok<PlatformConfigurationResponse>> ExecuteAsync(
        CancellationToken ct)
    {
        var current = await configuration.GetAsync(ct);
        return TypedResults.Ok(PlatformConfigurationMapping.ToResponse(
            current,
            links,
            HttpContext));
    }
}
