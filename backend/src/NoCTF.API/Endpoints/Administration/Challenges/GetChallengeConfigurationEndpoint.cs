using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed record ChallengeConfigurationResponse(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    GameMode Mode,
    string Json,
    int Revision,
    CompetitionStatus CompetitionStatus,
    DateTimeOffset UpdatedAt);

internal static class ChallengeConfigurationMapping
{
    public static ChallengeConfigurationResponse ToResponse(ChallengeConfigurationView view) =>
        new(
            view.CompetitionId,
            view.CompetitionChallengeId,
            view.Mode,
            view.Json,
            view.Revision,
            view.CompetitionStatus,
            view.UpdatedAt);
}

public sealed class GetChallengeConfigurationEndpoint(
    GetChallengeConfiguration get,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<Ok<ChallengeConfigurationResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/configuration");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeConfiguration_Get"));
        Summary(summary =>
        {
            summary.Summary = "Gets a competition challenge's game-mode configuration.";
            summary.Description = "Returns the versioned challenge configuration JSON visible to competition administrators.";
        });
    }

    public override async Task<
        Results<Ok<ChallengeConfigurationResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var view = await get.ExecuteAsync(
            competitionId,
            Route<Guid>("competitionChallengeId"),
            ct);
        return view is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeConfigurationMapping.ToResponse(view));
    }
}
