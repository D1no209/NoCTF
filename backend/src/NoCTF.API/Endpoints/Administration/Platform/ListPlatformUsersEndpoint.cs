using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Pagination;
using NoCTF.Application.Administration;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record PlatformUserResponse(
    Guid Id,
    string UserName,
    string Email,
    UserKindProtocol Kind,
    UserRoleProtocol Role,
    UserAccountStatusProtocol AccountStatus,
    int TokenVersion,
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    PlatformUserSsoBindingResponse? SsoBinding = null);

public sealed record PlatformUserSsoBindingResponse(
    Guid ProviderId,
    string? ProviderName,
    PublicSsoProtocol Protocol,
    string Subject,
    DateTimeOffset BoundAt);

public sealed class PlatformUserListResponse : ArrayResult<PlatformUserResponse>
{
    public PlatformUserListResponse() { }

    public PlatformUserListResponse(PlatformUserResponse[] items, int total)
        : base(items, total) { }
}

public sealed class ListPlatformUsersRequest : SearchRequest
{
    [QueryParam]
    public UserKindProtocol? Kind { get; set; }

    [QueryParam]
    public UserRoleProtocol? Role { get; set; }

    [QueryParam]
    public Guid? SsoProviderId { get; set; }
}

public sealed class ListPlatformUsersValidator : Validator<ListPlatformUsersRequest>
{
    public ListPlatformUsersValidator()
    {
        PaginationRules.AddSearch(this);
        RuleFor(request => request.Kind).IsInEnum().When(request => request.Kind is not null);
        RuleFor(request => request.Role).IsInEnum().When(request => request.Role is not null);
    }
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
internal static partial class PlatformUserMapping
{
    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial UserKindProtocol ToProtocol(UserKind value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial UserRoleProtocol ToProtocol(UserRole value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial UserAccountStatusProtocol ToProtocol(UserAccountStatus value);

    [MapperIgnoreTarget(nameof(PlatformUserResponse.SsoBinding))]
    private static partial PlatformUserResponse ToBaseResponse(PlatformUserView view);

    public static PlatformUserResponse ToResponse(
        PlatformUserView view,
        IReadOnlyDictionary<Guid, string>? providerNames = null)
    {
        var response = ToBaseResponse(view);
        if (view.SsoProviderId is not Guid providerId
            || view.SsoProtocol is not SsoProtocol protocol
            || view.SsoSubject is null
            || view.SsoBoundAt is not DateTimeOffset boundAt)
            return response;
        string? providerName = null;
        providerNames?.TryGetValue(providerId, out providerName);
        return response with
        {
            SsoBinding = new(
                providerId,
                providerName,
                protocol == SsoProtocol.Oidc
                    ? PublicSsoProtocol.Oidc
                    : PublicSsoProtocol.Cas,
                view.SsoSubject,
                boundAt)
        };
    }
}

public sealed class ListPlatformUsersEndpoint(
    ManagePlatform platform,
    ManageSsoProviders ssoProviders)
    : Endpoint<ListPlatformUsersRequest, Ok<PlatformUserListResponse>>
{
    public override void Configure()
    {
        Get("/admin/platform/users");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformListUsers"));
        Summary(summary =>
        {
            summary.Summary = "Lists platform users.";
            summary.Description = "Returns platform role and token-version metadata to administrators only.";
        });
    }

    public override async Task<Ok<PlatformUserListResponse>> ExecuteAsync(
        ListPlatformUsersRequest request,
        CancellationToken ct)
    {
        var page = await platform.ListUsersPageAsync(new(
            PaginationRules.Normalize(request.Keyword),
            request.Kind is null ? null : IdentityProtocolMapper.ToDomain(request.Kind.Value),
            request.Role is null ? null : IdentityProtocolMapper.ToDomain(request.Role.Value),
            request.Offset,
            request.Limit,
            request.Desc,
            request.SsoProviderId), ct);
        var sso = await ssoProviders.GetAsync(ct);
        var providerNames = sso.Providers.ToDictionary(
            provider => provider.Id,
            provider => provider.Name);
        return TypedResults.Ok(new PlatformUserListResponse(
            page.Items.Select(user => PlatformUserMapping.ToResponse(
                user,
                providerNames)).ToArray(),
            page.Total));
    }
}
