using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Authentication;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<PublicSsoProtocol>))]
public enum PublicSsoProtocol
{
    Oidc,
    Cas
}

public sealed record PublicSsoProviderResponse(
    Guid Id,
    string Name,
    string? IconUrl,
    PublicSsoProtocol Protocol);

public sealed record PublicSsoProviderListResponse(
    IReadOnlyList<PublicSsoProviderResponse> Items);

public sealed class ListSsoProvidersEndpoint(ManageSsoProviders management)
    : EndpointWithoutRequest<Ok<PublicSsoProviderListResponse>>
{
    public override void Configure()
    {
        Get("/auth/sso/providers");
        AllowAnonymous();
        Description(builder => builder.WithName("Authentication_SsoListProviders"));
        Summary(summary => { summary.Summary = "Lists enabled SSO providers that allow platform login."; summary.Description = summary.Summary; });
    }

    public override async Task<Ok<PublicSsoProviderListResponse>> ExecuteAsync(CancellationToken ct)
    {
        var configuration = await management.GetAsync(ct);
        var items = !configuration.Enabled
            ? []
            : configuration.Providers
                .Where(provider => provider.Enabled && provider.AllowLogin)
                .Select(provider => new PublicSsoProviderResponse(
                    provider.Id,
                    provider.Name,
                    provider.IconUrl,
                    provider.Protocol switch
                    {
                        SsoProtocol.Oidc => PublicSsoProtocol.Oidc,
                        SsoProtocol.Cas => PublicSsoProtocol.Cas,
                        _ => throw new InvalidOperationException(
                            $"Unsupported SSO protocol {provider.Protocol}.")
                    }))
                .ToArray();
        HttpContext.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(new PublicSsoProviderListResponse(items));
    }
}
