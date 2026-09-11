using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json.Serialization;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Tracks;

namespace NoCTF.API.Endpoints.Competitions.Tracks;

public sealed record CompetitionTrackResponse(
    string Key,
    string Name,
    bool IsDefault,
    bool IsPublicSelectable,
    bool IsInternal,
    bool EarnsScore,
    bool EarnsBlood,
    bool AffectsDynamicChallengeScore,
    bool VisibleOnLeaderboard,
    bool AffectsCompetitiveResults,
    bool RequiresInvitationCode,
    bool IsViewerTrack,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? InvitationCode = null);

public sealed record CompetitionTrackListResponse(
    Guid CompetitionId,
    GameModeProtocol Mode,
    bool Enabled,
    bool CanUpdate,
    IReadOnlyList<CompetitionTrackResponse> Items);

internal static class CompetitionTrackProtocolMapping
{
    public static CompetitionTrackResponse ToResponse(CompetitionTrackView view) => new(
        view.Key,
        view.Name,
        view.IsDefault,
        view.IsPublicSelectable,
        view.IsInternal,
        view.EarnsScore,
        view.EarnsBlood,
        view.AffectsDynamicChallengeScore,
        view.VisibleOnLeaderboard,
        view.AffectsCompetitiveResults,
        view.RequiresInvitationCode,
        view.IsViewerTrack,
        view.InvitationCode);

    public static CompetitionTrackListResponse ToResponse(CompetitionTracksView view) => new(
        view.CompetitionId,
        CompetitionProtocolMapper.ToProtocol(view.Mode),
        view.Enabled,
        view.CanUpdate,
        view.Tracks.Select(ToResponse).ToArray());
}

public sealed class ListCompetitionTracksEndpoint(
    GetCompetitionTracks get,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<CompetitionTrackListResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/tracks");
        AllowAnonymous();
        Description(builder => builder.WithName("ListCompetitionTracks"));
        Summary(summary =>
        {
            summary.Summary = "Lists competition tracks visible to the current participant.";
            summary.Description = "Returns public tracks and the authenticated participant team's own track.";
        });
    }

    public override async Task<Results<Ok<CompetitionTrackListResponse>, NotFound>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var view = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId == Guid.Empty ? null : user.UserId,
            includeInternal: false,
            includeInvitationCodes: false,
            cancellationToken);
        return view is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(CompetitionTrackProtocolMapping.ToResponse(view));
    }
}
