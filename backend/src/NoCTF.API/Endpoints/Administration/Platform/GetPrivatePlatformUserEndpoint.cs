using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.Application.Authentication.Privacy;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record PrivateActivityResponse(Guid Id, string Kind, DateTimeOffset OccurredAt, string? IpAddress,
    Guid? CompetitionId, Guid? GameplayFactId);
public sealed record PrivateSsoBindingResponse(
    Guid ProviderId,
    string? ProviderName,
    string? ProviderIconUrl,
    PublicSsoProtocol Protocol,
    string Subject,
    DateTimeOffset BoundAt);
public sealed record PrivateAccountResponse(
    CurrentUserSchoolIdentityResponse Identity,
    IReadOnlyList<PrivateActivityResponse> Activities,
    int RetentionDays,
    PrivateSsoBindingResponse? SsoBinding)
{
    internal static PrivateAccountResponse From(PrivateAccountDetails value) => new(
        new(value.Identity.FullName, value.Identity.StudentNumber),
        value.Activities.Select(item => new PrivateActivityResponse(item.Id, item.Kind.ToString(), item.OccurredAt,
            item.IpAddress, item.CompetitionId, item.GameplayFactId)).ToArray(),
        value.RetentionDays,
        value.SsoBinding is null
            ? null
            : new(
                value.SsoBinding.ProviderId,
                value.SsoBinding.ProviderName,
                value.SsoBinding.ProviderIconUrl,
                value.SsoBinding.Protocol == NoCTF.Domain.Identity.SsoProtocol.Oidc
                    ? PublicSsoProtocol.Oidc
                    : PublicSsoProtocol.Cas,
                value.SsoBinding.Subject,
                value.SsoBinding.BoundAt));
}

public sealed class GetPrivatePlatformUserEndpoint(AccountPrivacy privacy, IUserContext user)
    : EndpointWithoutRequest<Results<Ok<PrivateAccountResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/platform/users/{userId}/activity"); AuthSchemes("Bearer"); Roles("Administrator");
        Description(builder => builder.WithName("AdminGetPrivatePlatformUser"));
        Summary(summary => { summary.Summary = "Reads private school identity and recent source-IP activity.";
            summary.Description = "Platform administrators only. Returns at most 50 events within the configured retention window. Self-reported identity is not verified."; });
    }
    public override async Task<Results<Ok<PrivateAccountResponse>, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var value = await privacy.ReadPlatformAsync(user.UserId, Route<Guid>("userId"), ct);
        return value is null ? TypedResults.NotFound() : TypedResults.Ok(PrivateAccountResponse.From(value));
    }
}
