using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class OidcSsoProviderRequest
{
    public required string Issuer { get; set; }
    public required string DiscoveryUrl { get; set; }
    public required string ClientId { get; set; }
    public required string[] Scopes { get; set; }
    public required bool ReadUserInfo { get; set; }
    public required string DisplayNameClaim { get; set; }
}

public sealed class CasSsoProviderRequest
{
    public required string IdentityNamespace { get; set; }
    public required string LoginUrl { get; set; }
    public required string ServiceValidateUrl { get; set; }
    public required string DisplayNameAttribute { get; set; }
}

public class SsoProviderWriteRequest
{
    public required string Name { get; set; }
    public string? IconUrl { get; set; }
    public required SsoProtocolProtocol Protocol { get; set; }
    public required bool Enabled { get; set; }
    public required bool AllowLogin { get; set; }
    public required bool AllowBinding { get; set; }
    public required int TimeoutSeconds { get; set; }
    public required string[] AllowedHosts { get; set; }
    public OidcSsoProviderRequest? Oidc { get; set; }
    public CasSsoProviderRequest? Cas { get; set; }
}

public sealed class CreateSsoProviderRequest : SsoProviderWriteRequest;

public class SsoProviderWriteValidator<TRequest> : Validator<TRequest>
    where TRequest : SsoProviderWriteRequest
{
    public SsoProviderWriteValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(SsoRules.MaximumProviderNameLength);
        RuleFor(request => request.IconUrl).MaximumLength(SsoRules.MaximumUrlLength);
        RuleFor(request => request.Protocol).IsInEnum();
        RuleFor(request => request.TimeoutSeconds)
            .InclusiveBetween(SsoRules.MinimumTimeoutSeconds, SsoRules.MaximumTimeoutSeconds);
        RuleFor(request => request.AllowedHosts).NotNull()
            .Must(hosts => hosts.Length is >= 1 and <= SsoRules.MaximumAllowedHosts);
        RuleForEach(request => request.AllowedHosts).NotEmpty().MaximumLength(253);
        RuleFor(request => request).Must(request => request.Protocol switch
        {
            SsoProtocolProtocol.Oidc => request.Oidc is not null && request.Cas is null,
            SsoProtocolProtocol.Cas => request.Cas is not null && request.Oidc is null,
            _ => false
        }).WithMessage("Exactly one protocol configuration matching the provider protocol is required.");
    }
}

public sealed class CreateSsoProviderValidator : SsoProviderWriteValidator<CreateSsoProviderRequest>;

internal static class SsoProviderRequestMapping
{
    internal static SsoProviderDraft ToDraft(SsoProviderWriteRequest request) => new(
        request.Name,
        request.IconUrl,
        SsoAdministrationMapping.ToDomain(request.Protocol),
        request.Enabled,
        request.AllowLogin,
        request.AllowBinding,
        request.TimeoutSeconds,
        request.AllowedHosts,
        request.Oidc is null ? null : new OidcSsoProviderDraft(
            request.Oidc.Issuer,
            request.Oidc.DiscoveryUrl,
            request.Oidc.ClientId,
            request.Oidc.Scopes,
            request.Oidc.ReadUserInfo,
            request.Oidc.DisplayNameClaim),
        request.Cas is null ? null : new CasSsoProviderDraft(
            request.Cas.IdentityNamespace,
            request.Cas.LoginUrl,
            request.Cas.ServiceValidateUrl,
            request.Cas.DisplayNameAttribute));
}

public sealed class CreateSsoProviderEndpoint(
    ManageSsoProviders management,
    IUserContext user)
    : Endpoint<CreateSsoProviderRequest,
        Results<Ok<SsoConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/sso/providers");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformSsoCreateProvider"));
        Summary(summary => summary.Summary = "Creates a disabled or ready SSO provider definition.");
    }

    public override async Task<Results<Ok<SsoConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        CreateSsoProviderRequest request,
        CancellationToken ct)
    {
        var result = await management.CreateProviderAsync(
            SsoProviderRequestMapping.ToDraft(request), user.UserId, ct);
        return result.State == SsoConfigurationMutationState.Updated
            ? TypedResults.Ok(SsoAdministrationMapping.ToResponse(result.Configuration!))
            : SsoAdministrationMapping.Problem(result.State);
    }
}
