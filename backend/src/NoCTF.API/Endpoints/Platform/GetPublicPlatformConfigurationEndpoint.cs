using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Storage;
using NoCTF.Application.Admission;
using NoCTF.API.Serialization;
using NoCTF.Domain.Platform;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Platform;

public sealed record PublicImageUploadLimitsResponse(
    long MaximumAvatarBytes,
    long MaximumWallpaperBytes);

[JsonConverter(typeof(StrictPascalCaseEnumConverter<HumanVerificationProviderProtocol>))]
public enum HumanVerificationProviderProtocol
{
    None,
    Cap,
    Turnstile
}

public sealed record PublicHumanVerificationResponse(
    HumanVerificationProviderProtocol Provider,
    string? SiteKey,
    string? ApiEndpoint,
    bool RuntimeRequired);

public sealed record PublicPlatformConfigurationResponse(
    string Name,
    string? Description,
    string? LogoUrl,
    PublicImageUploadLimitsResponse ImageUploadLimits,
    PublicHumanVerificationResponse HumanVerification);

internal static class PublicPlatformConfigurationMapping
{
    public static PublicPlatformConfigurationResponse ToResponse(
        PlatformConfigurationView configuration,
        LinkGenerator links,
        HttpContext httpContext,
        FileUploadLimits uploadLimits,
        HumanVerificationConfigurationView humanVerification) =>
        new(
            configuration.Name,
            configuration.Description,
            LogoUrl(configuration, links, httpContext),
            new(
                uploadLimits.MaximumAvatarBytes,
                uploadLimits.MaximumWallpaperBytes),
            MapHumanVerification(humanVerification));

    private static PublicHumanVerificationResponse MapHumanVerification(
        HumanVerificationConfigurationView configuration) =>
        !configuration.Enabled || !configuration.Ready
            ? new(HumanVerificationProviderProtocol.None, null, null, false)
            : configuration.Provider switch
        {
            HumanVerificationProvider.None => new(
                HumanVerificationProviderProtocol.None,
                null,
                null,
                false),
            HumanVerificationProvider.Cap => new(
                HumanVerificationProviderProtocol.Cap,
                configuration.CapSiteKey,
                CapApiEndpoint(configuration),
                configuration.RuntimeEnabled),
            HumanVerificationProvider.Turnstile => new(
                HumanVerificationProviderProtocol.Turnstile,
                configuration.TurnstileSiteKey,
                null,
                configuration.RuntimeEnabled),
            _ => throw new InvalidOperationException(
                $"Unsupported human verification provider: {configuration.Provider}.")
        };

    private static string CapApiEndpoint(
        HumanVerificationConfigurationView configuration) =>
        new Uri(
            new Uri(configuration.CapServerUrl.TrimEnd('/') + "/", UriKind.Absolute),
            $"{Uri.EscapeDataString(configuration.CapSiteKey)}/").AbsoluteUri;

    public static HumanVerificationProviderProtocol ToProtocol(
        HumanVerificationProvider provider) => provider switch
        {
            HumanVerificationProvider.None => HumanVerificationProviderProtocol.None,
            HumanVerificationProvider.Cap => HumanVerificationProviderProtocol.Cap,
            HumanVerificationProvider.Turnstile => HumanVerificationProviderProtocol.Turnstile,
            _ => throw new InvalidOperationException(
                $"Unsupported human verification provider: {provider}.")
        };

    public static HumanVerificationProvider ToDomain(
        HumanVerificationProviderProtocol provider) => provider switch
        {
            HumanVerificationProviderProtocol.None => HumanVerificationProvider.None,
            HumanVerificationProviderProtocol.Cap => HumanVerificationProvider.Cap,
            HumanVerificationProviderProtocol.Turnstile => HumanVerificationProvider.Turnstile,
            _ => throw new InvalidOperationException(
                $"Unsupported human verification provider: {provider}.")
        };

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
    ManageHumanVerificationConfiguration humanVerification,
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
            summary.Description = "Exposes branding, deployment-selected image upload limits and public human-verification settings without provider secrets or storage metadata.";
        });
    }

    public override async Task<Ok<PublicPlatformConfigurationResponse>> ExecuteAsync(
        CancellationToken ct)
    {
        var current = await configuration.GetAsync(ct);
        var verification = await humanVerification.GetAsync(ct);
        return TypedResults.Ok(PublicPlatformConfigurationMapping.ToResponse(
            current,
            links,
            HttpContext,
            uploadLimits,
            verification));
    }
}
