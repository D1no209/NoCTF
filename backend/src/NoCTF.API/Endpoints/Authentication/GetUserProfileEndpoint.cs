using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Competitions.Progression;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class GetUserProfileRequest
{
    public Guid UserId { get; set; }
}

public sealed record PublicUserProfileResponse(
    Guid UserId,
    string UserName,
    string? Description,
    string? AvatarUrl,
    string? ProfileCoverUrl,
    int CompetitionCount,
    int FinishedCompetitionCount,
    int SuccessfulChallengeCount,
    IReadOnlyList<PublicUserModeSummaryResponse> Modes,
    IReadOnlyList<PublicUserDirectionSummaryResponse> Directions,
    IReadOnlyList<PublicUserCompetitionSummaryResponse> RecentCompetitions,
    IReadOnlyList<NoCTF.API.Endpoints.Competitions.ProgressionBadgeDisplayContract> Badges);

public sealed record PublicUserModeSummaryResponse(
    GameModeProtocol Mode,
    int CompetitionCount);

public sealed record PublicUserDirectionSummaryResponse(
    string Direction,
    int SuccessfulChallengeCount);

public sealed record PublicUserCompetitionSummaryResponse(
    Guid CompetitionId,
    string Title,
    string TeamName,
    GameModeProtocol Mode,
    CompetitionStatusProtocol Status,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt);

public sealed class GetUserProfileEndpoint(
    GetPublicUserProfile getProfile,
    IProgressionPlayerReader progression,
    LinkGenerator links)
    : Endpoint<GetUserProfileRequest, Results<Ok<PublicUserProfileResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/users/{userId}");
        AllowAnonymous();
        Description(builder => builder.WithName("UserProfile_Get"));
        Summary(summary => summary.Summary = "Returns a user's public profile.");
    }

    public override async Task<Results<Ok<PublicUserProfileResponse>, NotFound>> ExecuteAsync(
        GetUserProfileRequest request,
        CancellationToken ct)
    {
        var profile = await getProfile.ExecuteAsync(request.UserId, ct);
        if (profile is null)
            return TypedResults.NotFound();

        var avatarUrl = CurrentUserMapping.AvatarUrl(
            profile.Id,
            profile.AvatarFileId,
            links,
            HttpContext);
        var profileCoverUrl = CurrentUserMapping.ProfileCoverUrl(
            profile.Id,
            profile.ProfileCoverFileId,
            links,
            HttpContext);
        return TypedResults.Ok(new PublicUserProfileResponse(
            profile.Id,
            profile.UserName,
            profile.Description,
            avatarUrl,
            profileCoverUrl,
            profile.CompetitionCount,
            profile.FinishedCompetitionCount,
            profile.SuccessfulChallengeCount,
            (profile.Modes ?? []).Select(item => new PublicUserModeSummaryResponse(
                CompetitionProtocolMapper.ToProtocol(item.Mode),
                item.CompetitionCount)).ToList(),
            (profile.Directions ?? []).Select(item => new PublicUserDirectionSummaryResponse(
                item.Direction,
                item.SuccessfulChallengeCount)).ToList(),
            (profile.RecentCompetitions ?? []).Select(item => new PublicUserCompetitionSummaryResponse(
                item.CompetitionId,
                item.Title,
                item.TeamName,
                CompetitionProtocolMapper.ToProtocol(item.Mode),
                CompetitionProtocolMapper.ToProtocol(item.Status),
                item.StartAt,
                item.EndAt)).ToList(),
            (await progression.ReadPublicUserBadgesAsync(request.UserId, ct))
                .Select(NoCTF.API.Endpoints.Competitions.ProgressionBadgeDisplayProtocol.ToContract)
                .ToArray()));
    }

}
