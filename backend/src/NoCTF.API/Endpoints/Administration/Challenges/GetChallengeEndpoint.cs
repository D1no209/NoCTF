using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class GetAdminChallengeRequest
{
    [QueryParam]
    public bool IncludeDeleted { get; set; }
}

public sealed class GetChallengeEndpoint(
    GetChallenge get,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<GetAdminChallengeRequest, Results<Ok<ChallengeResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCompetitionChallenge"));
        Summary(summary =>
        {
            summary.Summary = "Gets a competition challenge.";
            summary.Description = "Returns management details for published or unpublished competition challenges.";
        });
    }

    public override async Task<Results<Ok<ChallengeResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        GetAdminChallengeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var item = await get.ExecuteAsync(
            competitionId,
            Route<Guid>("competitionChallengeId"),
            includeUnpublished: true,
            request.IncludeDeleted,
            ct);

        return item is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeMapper.ToResponse(item));
    }
}
