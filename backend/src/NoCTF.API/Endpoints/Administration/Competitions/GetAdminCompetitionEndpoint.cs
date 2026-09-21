using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Competitions.Permissions;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.API.Endpoints.Competitions.Tracks;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Authentication.Sso;
using NoCTF.API.Endpoints.Authentication;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed record CompetitionConfigurationResponse(
    Guid CompetitionId,
    GameModeProtocol Mode,
    string Json,
    CompetitionStatusProtocol CompetitionStatus,
    DateTimeOffset UpdatedAt);

internal static class CompetitionConfigurationMapping
{
    public static CompetitionConfigurationResponse ToResponse(
        CompetitionConfigurationView view) => new(
        view.CompetitionId,
        CompetitionProtocolMapper.ToProtocol(view.Mode),
        view.Json,
        CompetitionProtocolMapper.ToProtocol(view.CompetitionStatus),
        view.UpdatedAt);
}

public sealed record CompetitionPermissionsResponse(
    Guid CompetitionId,
    Guid OwnerId,
    IReadOnlyList<Guid> ManagerIds,
    IReadOnlyList<Guid> JudgeIds,
    IReadOnlyList<Guid> ObserverIds);

internal static class CompetitionPermissionsMapper
{
    public static CompetitionPermissionsResponse ToResponse(
        CompetitionPermissionSnapshot snapshot) => new(
        snapshot.CompetitionId,
        snapshot.OwnerId,
        snapshot.ManagerIds,
        snapshot.JudgeIds,
        snapshot.ObserverIds);
}

public sealed record CompetitionLeaderboardVisibilityResponse(
    Guid CompetitionId,
    LeaderboardVisibilityProtocol EffectiveVisibility,
    DateTimeOffset? FrozenStartAt,
    DateTimeOffset? HiddenStartAt);

internal static class CompetitionLeaderboardVisibilityMapper
{
    public static CompetitionLeaderboardVisibilityResponse ToResponse(
        CompetitionVisibilityConfigurationView view) => new(
        view.CompetitionId,
        CompetitionProtocolMapper.ToProtocol(view.EffectiveVisibility),
        view.FrozenStartAt,
        view.HiddenStartAt);
}

public sealed record AdminCompetitionCapabilitiesResponse(
    bool CanObserve,
    bool CanModerate,
    bool CanManagePermissions);

public sealed record CompetitionSsoProviderResponse(
    Guid Id,
    string Name,
    string? IconUrl,
    PublicSsoProtocol Protocol,
    bool Enabled,
    bool AllowBinding);

public sealed record AdminCompetitionResponse(
    CompetitionResponse Competition,
    CompetitionConfigurationResponse ModeConfiguration,
    CompetitionTrackListResponse Tracks,
    CompetitionPermissionsResponse? Permissions,
    CompetitionLeaderboardVisibilityResponse LeaderboardVisibility,
    IReadOnlyList<CompetitionSsoProviderResponse> SsoProviders,
    AdminCompetitionCapabilitiesResponse Capabilities);

public sealed class GetAdminCompetitionEndpoint(
    GetAdminCompetition get,
    GetCompetitionConfiguration getConfiguration,
    GetCompetitionTracks getTracks,
    GetCompetitionPermissions getPermissions,
    GetCompetitionVisibility getVisibility,
    ManageSsoProviders ssoProviders,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Ok<AdminCompetitionResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCompetition"));
        Summary(summary =>
        {
            summary.Summary = "Gets an administratively visible competition.";
            summary.Description = "Returns draft or public competition metadata when the caller has resource access.";
        });
    }

    public override async Task<Results<Ok<AdminCompetitionResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var view = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            includeDeleted: true,
            ct: ct);
        if (view is null)
            return TypedResults.NotFound();
        var role = await CompetitionAdministrationRoleResolver.ResolveAsync(
            view,
            user,
            authorizer,
            ct,
            accessAlreadyEstablished: true);
        var canModerate = await authorizer.CanModerateAsync(user.UserId, view.Id, ct);
        var configuration = await getConfiguration.ExecuteAsync(view.Id, ct);
        var tracks = await getTracks.ExecuteAsync(
            view.Id,
            user.UserId,
            includeInternal: true,
            includeInvitationCodes: canModerate,
            ct);
        var permissions = await getPermissions.ExecuteAsync(
            view.Id,
            user.UserId,
            user.IsAdministrator,
            ct);
        var visibility = await getVisibility.ExecuteAsync(
            view.Id,
            timeProvider.GetUtcNow(),
            ct);
        var sso = await ssoProviders.GetAsync(ct);
        if (configuration is null || tracks is null || visibility is null)
            return TypedResults.NotFound();
        var competition = CompetitionMapper.ToResponse(
            view,
            timeProvider.GetUtcNow()) with
        {
            AdministrationRole = role
        };
        return TypedResults.Ok(new AdminCompetitionResponse(
            competition,
            CompetitionConfigurationMapping.ToResponse(configuration),
            CompetitionTrackProtocolMapping.ToResponse(tracks),
            permissions.State == CompetitionPermissionSnapshotState.Found
                ? CompetitionPermissionsMapper.ToResponse(permissions.Snapshot!)
                : null,
            CompetitionLeaderboardVisibilityMapper.ToResponse(visibility),
            sso.Providers.Select(provider => new CompetitionSsoProviderResponse(
                provider.Id,
                provider.Name,
                provider.IconUrl,
                provider.Protocol == NoCTF.Domain.Identity.SsoProtocol.Oidc
                    ? PublicSsoProtocol.Oidc
                    : PublicSsoProtocol.Cas,
                provider.Enabled,
                provider.AllowBinding)).ToArray(),
            new(true, canModerate,
                permissions.State == CompetitionPermissionSnapshotState.Found)));
    }
}
