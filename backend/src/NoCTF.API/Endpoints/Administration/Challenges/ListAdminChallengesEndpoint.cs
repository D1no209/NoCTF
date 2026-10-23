using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Competitions.Scoring;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Endpoints.Competitions;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class ListAdminChallengesRequest
{
    public Guid CompetitionId { get; set; }
    [QueryParam]
    public bool IncludeDeleted { get; set; }
}
public sealed record AdminChallengeListResponse(IReadOnlyList<ChallengeSummaryResponse> Items,
    IReadOnlyList<CompetitionScorePresetResponse> ScorePresets,
    LeaderboardVisibilityProtocol LeaderboardVisibility = LeaderboardVisibilityProtocol.Normal,
    LeaderboardDataScopeProtocol DataScope = LeaderboardDataScopeProtocol.Live);

public sealed class ListAdminChallengesEndpoint(
    ListChallenges list,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    ManageCompetitionScorePresets presets)
    : Endpoint<ListAdminChallengesRequest, Results<Ok<AdminChallengeListResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitionChallenges"));
        Summary(summary =>
        {
            summary.Summary = "Lists all competition challenges.";
            summary.Description = "Includes unpublished challenge instances for authorized competition observers.";
        });
    }

    public override async Task<Results<Ok<AdminChallengeListResponse>, ForbidHttpResult>> ExecuteAsync(
        ListAdminChallengesRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var items = await list.ExecuteAsync(
            competitionId,
            includeUnpublished: true,
            request.IncludeDeleted,
            ct);
        var catalog = await presets.ListAsync(competitionId, ct);
        return TypedResults.Ok(new AdminChallengeListResponse(ChallengeMapper.ToListResponse(items).Items,
            catalog.Items?.Select(CompetitionScorePresetResponses.Map).ToArray() ?? []));
    }
}
