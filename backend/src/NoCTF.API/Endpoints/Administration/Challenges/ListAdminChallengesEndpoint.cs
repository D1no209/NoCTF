using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class ListAdminChallengesRequest
{
    [QueryParam]
    public bool IncludeDeleted { get; set; }
}

public sealed class ListAdminChallengesEndpoint(
    ListChallenges list,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ListAdminChallengesRequest, Results<Ok<ChallengeListResponse>, ForbidHttpResult>>
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

    public override async Task<Results<Ok<ChallengeListResponse>, ForbidHttpResult>> ExecuteAsync(
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
        return TypedResults.Ok(ChallengeMapper.ToListResponse(items));
    }
}
