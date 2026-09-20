using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Authentication;

public sealed record MySsoBindingResponse(
    Guid ProviderId,
    string ProviderName,
    PublicSsoProtocol Protocol,
    string IdentityNamespace,
    string Subject,
    DateTimeOffset BoundAt);

public sealed record BindableSsoProviderResponse(
    Guid Id,
    string Name,
    PublicSsoProtocol Protocol);

public sealed record MySsoBindingConfigurationResponse(
    MySsoBindingResponse? Binding,
    IReadOnlyList<BindableSsoProviderResponse> Providers);

public sealed class GetMySsoBindingEndpoint(GetSsoBinding getBinding, IUserContext user)
    : EndpointWithoutRequest<Ok<MySsoBindingConfigurationResponse>>
{
    public override void Configure()
    {
        Get("/auth/me/sso-binding");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("Authentication_SsoGetMyBinding"));
        Summary(summary => summary.Summary = "Returns the current user's external identity binding and bindable providers.");
    }

    public override async Task<Ok<MySsoBindingConfigurationResponse>> ExecuteAsync(CancellationToken ct)
    {
        var result = await getBinding.ExecuteAsync(user.UserId, ct);
        var binding = result.Binding is null ? null : new MySsoBindingResponse(
            result.Binding.ProviderId,
            result.Binding.ProviderName,
            ToProtocol(result.Binding.Protocol),
            result.Binding.IdentityNamespace,
            result.Binding.Subject,
            result.Binding.BoundAt);
        var providers = result.Providers.Select(provider => new BindableSsoProviderResponse(
            provider.Id,
            provider.Name,
            ToProtocol(provider.Protocol))).ToArray();
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        return TypedResults.Ok(new MySsoBindingConfigurationResponse(binding, providers));
    }

    private static PublicSsoProtocol ToProtocol(NoCTF.Domain.Identity.SsoProtocol protocol) =>
        protocol == NoCTF.Domain.Identity.SsoProtocol.Oidc
            ? PublicSsoProtocol.Oidc
            : PublicSsoProtocol.Cas;
}
