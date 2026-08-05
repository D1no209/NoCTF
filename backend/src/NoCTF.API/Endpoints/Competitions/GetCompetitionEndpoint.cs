using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Competitions;

public sealed record CompetitionResponse(
    Guid Id,
    string Title,
    string? Description,
    GameMode Mode,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    CompetitionStatus Status,
    bool TeamRegistrationAutoApprove,
    int MaxTeamMembers,
    int MaxConcurrentRuntimeInstancesPerTeam,
    Guid OwnerId,
    CompetitionLeaderboardVisibility LeaderboardVisibility);

internal static class CompetitionMapper
{
    public static CompetitionResponse ToResponse(CompetitionView view) =>
        new(
            view.Id,
            view.Title,
            view.Description,
            view.Mode,
            view.StartTime,
            view.EndTime,
            view.Status,
            view.TeamRegistrationAutoApprove,
            view.MaxTeamMembers,
            view.MaxConcurrentRuntimeInstancesPerTeam,
            view.OwnerId,
            CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
                view.Status,
                view.LeaderboardVisibility,
                view.LeaderboardVisibilityStartsAt,
                DateTimeOffset.UtcNow));
}

public sealed class GetCompetitionRequest { public Guid CompetitionId { get; set; } }

public sealed class GetCompetitionEndpoint(GetCompetition get)
    : Endpoint<GetCompetitionRequest, Results<Ok<CompetitionResponse>, NotFound>>
{
    public override void Configure() { Get("/competitions/{competitionId}"); AllowAnonymous(); }

    public override async Task<Results<Ok<CompetitionResponse>, NotFound>> ExecuteAsync(GetCompetitionRequest request, CancellationToken ct)
    {
        var view = await get.ExecuteAsync(Route<Guid>("competitionId"), false, ct);
        return view is null ? TypedResults.NotFound() : TypedResults.Ok(CompetitionMapper.ToResponse(view));
    }
}
