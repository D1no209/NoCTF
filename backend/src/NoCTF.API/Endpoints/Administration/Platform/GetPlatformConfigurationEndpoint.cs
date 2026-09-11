using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Endpoints.Platform;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Runtime.PublicAccess;
using Microsoft.Extensions.Options;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record PlatformBrandingResponse(
    string Name,
    string? Description,
    string? LogoUrl,
    DateTimeOffset UpdatedAt);

internal static class PlatformConfigurationMapping
{
    public static PlatformBrandingResponse ToResponse(
        PlatformConfigurationView configuration,
        LinkGenerator links,
        HttpContext httpContext) =>
        new(
            configuration.Name,
            configuration.Description,
            PublicPlatformConfigurationMapping.LogoUrl(configuration, links, httpContext),
            configuration.UpdatedAt);
}

public sealed record AdminHumanVerificationConfigurationResponse(
    bool Enabled,
    HumanVerificationProviderProtocol Provider,
    bool Available);

internal static class AdminHumanVerificationConfigurationMapping
{
    public static AdminHumanVerificationConfigurationResponse ToResponse(
        PlatformConfigurationView configuration,
        HumanVerificationOptions options) =>
        new(
            configuration.HumanVerificationEnabled,
            PublicPlatformConfigurationMapping.ToProtocol(options.Provider),
            options.Provider != HumanVerificationProvider.None);
}

public sealed record AdminPlatformConfigurationResponse(
    PlatformBrandingResponse Branding,
    AdminHumanVerificationConfigurationResponse HumanVerification,
    EmailVerificationConfigurationResponse EmailVerification,
    PublicGatewayConfigurationResponse PublicGateway,
    string PublicGatewayStatusUrl);

public sealed class GetPlatformConfigurationEndpoint(
    ManagePlatformConfiguration configuration,
    ManageEmailVerificationConfiguration emailVerification,
    ManagePublicGateway publicGateway,
    LinkGenerator links,
    IOptions<HumanVerificationOptions> humanVerification)
    : EndpointWithoutRequest<Ok<AdminPlatformConfigurationResponse>>
{
    public override void Configure()
    {
        Get("/admin/platform/configuration");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformGetConfiguration"));
        Summary(summary =>
        {
            summary.Summary = "Returns editable platform configuration sections.";
            summary.Description = "Returns branding, human verification, email delivery and public gateway configuration without deployment secrets.";
        });
    }

    public override async Task<Ok<AdminPlatformConfigurationResponse>> ExecuteAsync(
        CancellationToken ct)
    {
        var current = await configuration.GetAsync(ct);
        var email = await emailVerification.GetAsync(ct);
        var gateway = await publicGateway.GetAsync(ct);
        return TypedResults.Ok(new AdminPlatformConfigurationResponse(
            PlatformConfigurationMapping.ToResponse(current, links, HttpContext),
            AdminHumanVerificationConfigurationMapping.ToResponse(
                current, humanVerification.Value),
            EmailVerificationConfigurationMapping.ToResponse(email),
            PublicGatewayConfigurationMapping.ToResponse(gateway),
            "/api/v1/admin/platform/public-gateway/status"));
    }
}
