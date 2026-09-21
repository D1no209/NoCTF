using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Platform;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<SsoProtocolProtocol>))]
public enum SsoProtocolProtocol
{
    Oidc,
    Cas
}

public sealed record OidcSsoProviderResponse(
    string Issuer,
    string DiscoveryUrl,
    string ClientId,
    IReadOnlyList<string> Scopes,
    bool ReadUserInfo,
    string DisplayNameClaim,
    bool SecretConfigured);

public sealed record CasSsoProviderResponse(
    string IdentityNamespace,
    string LoginUrl,
    string ServiceValidateUrl,
    string DisplayNameAttribute);

public sealed record SsoProviderResponse(
    Guid Id,
    string Name,
    string? IconUrl,
    SsoProtocolProtocol Protocol,
    bool Enabled,
    bool AllowLogin,
    bool AllowBinding,
    int TimeoutSeconds,
    IReadOnlyList<string> AllowedHosts,
    OidcSsoProviderResponse? Oidc,
    CasSsoProviderResponse? Cas);

public sealed record SsoConfigurationResponse(
    bool Enabled,
    string PublicBaseUrl,
    IReadOnlyList<SsoProviderResponse> Providers,
    DateTimeOffset UpdatedAt);

internal static class SsoAdministrationMapping
{
    internal static SsoConfigurationResponse ToResponse(SsoConfigurationView view) => new(
        view.Enabled,
        view.PublicBaseUrl,
        view.Providers.Select(provider => new SsoProviderResponse(
            provider.Id,
            provider.Name,
            provider.IconUrl,
            ToProtocol(provider.Protocol),
            provider.Enabled,
            provider.AllowLogin,
            provider.AllowBinding,
            provider.TimeoutSeconds,
            provider.AllowedHosts,
            provider.Oidc is null ? null : new OidcSsoProviderResponse(
                provider.Oidc.Issuer,
                provider.Oidc.DiscoveryUrl,
                provider.Oidc.ClientId,
                provider.Oidc.Scopes,
                provider.Oidc.ReadUserInfo,
                provider.Oidc.DisplayNameClaim,
                provider.Oidc.SecretConfigured),
            provider.Cas is null ? null : new CasSsoProviderResponse(
                provider.Cas.IdentityNamespace,
                provider.Cas.LoginUrl,
                provider.Cas.ServiceValidateUrl,
                provider.Cas.DisplayNameAttribute))).ToArray(),
        view.UpdatedAt);

    internal static SsoProtocolProtocol ToProtocol(SsoProtocol value) => value switch
    {
        SsoProtocol.Oidc => SsoProtocolProtocol.Oidc,
        SsoProtocol.Cas => SsoProtocolProtocol.Cas,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    internal static SsoProtocol ToDomain(SsoProtocolProtocol value) => value switch
    {
        SsoProtocolProtocol.Oidc => SsoProtocol.Oidc,
        SsoProtocolProtocol.Cas => SsoProtocol.Cas,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    internal static ProblemHttpResult Problem(SsoConfigurationMutationState state)
    {
        var status = state switch
        {
            SsoConfigurationMutationState.ProviderNotFound => StatusCodes.Status404NotFound,
            SsoConfigurationMutationState.DuplicateTrustBoundary
                or SsoConfigurationMutationState.TrustBoundaryImmutable
                or SsoConfigurationMutationState.SecretRequired
                or SsoConfigurationMutationState.ProviderLimitReached => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return TypedResults.Problem(
            statusCode: status,
            title: "The SSO configuration could not be updated.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = state.ToString()
            });
    }
}

public sealed class GetSsoConfigurationEndpoint(ManageSsoProviders management)
    : EndpointWithoutRequest<Ok<SsoConfigurationResponse>>
{
    public override void Configure()
    {
        Get("/admin/platform/sso");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformSsoGetConfiguration"));
        Summary(summary => summary.Summary = "Returns SSO configuration without provider secrets.");
    }

    public override async Task<Ok<SsoConfigurationResponse>> ExecuteAsync(CancellationToken ct) =>
        TypedResults.Ok(SsoAdministrationMapping.ToResponse(await management.GetAsync(ct)));
}
