using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Endpoints.Platform;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.EmailVerification;

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
    bool RuntimeEnabled,
    bool EvaluationEnabled,
    HumanVerificationProviderProtocol Provider,
    bool Ready,
    string CapServerUrl,
    string CapSiteKey,
    bool CapSecretConfigured,
    string TurnstileSiteKey,
    bool TurnstileSecretConfigured,
    IReadOnlyList<string> TurnstileAllowedHostnames,
    DateTimeOffset UpdatedAt);

internal static class AdminHumanVerificationConfigurationMapping
{
    public static AdminHumanVerificationConfigurationResponse ToResponse(
        HumanVerificationConfigurationView configuration) =>
        new(
            configuration.Enabled,
            configuration.RuntimeEnabled,
            configuration.EvaluationEnabled,
            PublicPlatformConfigurationMapping.ToProtocol(configuration.Provider),
            configuration.Ready,
            configuration.CapServerUrl,
            configuration.CapSiteKey,
            configuration.CapSecretConfigured,
            configuration.TurnstileSiteKey,
            configuration.TurnstileSecretConfigured,
            configuration.TurnstileAllowedHostnames,
            configuration.UpdatedAt);
}

public sealed record AdminPlatformConfigurationResponse(
    PlatformBrandingResponse Branding,
    AdminHumanVerificationConfigurationResponse HumanVerification,
    EmailVerificationConfigurationResponse EmailVerification,
    PlatformExperimentalFeaturesResponse ExperimentalFeatures);

public sealed record PlatformExperimentalFeaturesResponse(
    bool CtfPatchVerificationEnabled);

public sealed class GetPlatformConfigurationEndpoint(
    ManagePlatformConfiguration configuration,
    ManageHumanVerificationConfiguration humanVerification,
    ManageEmailVerificationConfiguration emailVerification,
    LinkGenerator links)
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
            summary.Description = "Returns branding, human verification, email delivery and experimental feature configuration without provider or delivery secrets.";
        });
    }

    public override async Task<Ok<AdminPlatformConfigurationResponse>> ExecuteAsync(
        CancellationToken ct)
    {
        var current = await configuration.GetAsync(ct);
        var verification = await humanVerification.GetAsync(ct);
        var email = await emailVerification.GetAsync(ct);
        return TypedResults.Ok(new AdminPlatformConfigurationResponse(
            PlatformConfigurationMapping.ToResponse(current, links, HttpContext),
            AdminHumanVerificationConfigurationMapping.ToResponse(verification),
            EmailVerificationConfigurationMapping.ToResponse(email),
            new(current.CtfPatchVerificationEnabled)));
    }
}
