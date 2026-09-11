using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed record AdminCompetitionChallengeResponse(
    ChallengeResponse Challenge,
    GameModeProtocol Mode,
    CompetitionStatusProtocol CompetitionStatus,
    string RulesJson);

public sealed class GetAdminChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    [QueryParam]
    public bool IncludeDeleted { get; set; }
}

public sealed class GetChallengeEndpoint(
    GetChallenge get,
    GetChallengeConfiguration getConfiguration,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<GetAdminChallengeRequest,
        Results<Ok<AdminCompetitionChallengeResponse>, NotFound, ForbidHttpResult>>
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

    public override async Task<Results<Ok<AdminCompetitionChallengeResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        GetAdminChallengeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var item = await get.ExecuteAsync(
            competitionId,
            request.CompetitionChallengeId,
            includeUnpublished: true,
            request.IncludeDeleted,
            ct);

        var configuration = await getConfiguration.ExecuteAsync(
            competitionId,
            request.CompetitionChallengeId,
            ct);
        return item is null || configuration is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new AdminCompetitionChallengeResponse(
                ChallengeMapper.ToResponse(item),
                CompetitionProtocolMapper.ToProtocol(configuration.Mode),
                CompetitionProtocolMapper.ToProtocol(configuration.CompetitionStatus),
                configuration.Json));
    }
}
