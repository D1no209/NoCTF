using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionLeaderboardVisibilityRequest
{
    public CompetitionLeaderboardVisibility? Visibility { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public int? ExpectedRevision { get; set; }
    public string? Reason { get; set; }
}

public sealed class UpdateCompetitionLeaderboardVisibilityValidator
    : Validator<UpdateCompetitionLeaderboardVisibilityRequest>
{
    public UpdateCompetitionLeaderboardVisibilityValidator()
    {
        RuleFor(request => request.Visibility).NotNull().IsInEnum();
        RuleFor(request => request.ExpectedRevision).NotNull().GreaterThanOrEqualTo(0);
        RuleFor(request => request.Reason).MaximumLength(500);
    }
}

public sealed record CompetitionLeaderboardVisibilityFailureResponse(
    CompetitionVisibilityMutationState Code,
    CompetitionLeaderboardVisibilityResponse? Current);

public sealed class UpdateCompetitionLeaderboardVisibilityEndpoint(
    UpdateCompetitionVisibility update,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<UpdateCompetitionLeaderboardVisibilityRequest, Results<
        Ok<CompetitionLeaderboardVisibilityResponse>,
        Conflict<CompetitionLeaderboardVisibilityFailureResponse>,
        NotFound,
        ForbidHttpResult,
        ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/leaderboard-visibility");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminUpdateCompetitionLeaderboardVisibility"));
        Summary(summary =>
        {
            summary.Summary = "Schedules or immediately applies leaderboard visibility.";
            summary.Description = "Uses a dedicated revision fence and persists the exact Frozen cutoff snapshot when the restriction takes effect.";
        });
    }

    public override async Task<Results<
        Ok<CompetitionLeaderboardVisibilityResponse>,
        Conflict<CompetitionLeaderboardVisibilityFailureResponse>,
        NotFound,
        ForbidHttpResult,
        ProblemHttpResult>> ExecuteAsync(
        UpdateCompetitionLeaderboardVisibilityRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await update.ExecuteAsync(new(
            competitionId,
            request.Visibility!.Value,
            request.StartsAt,
            request.ExpectedRevision!.Value,
            user.UserId,
            request.Reason,
            DateTimeOffset.UtcNow), ct);
        var current = result.Configuration is null
            ? null
            : CompetitionLeaderboardVisibilityMapper.ToResponse(result.Configuration);
        return result.State switch
        {
            CompetitionVisibilityMutationState.Updated => TypedResults.Ok(current!),
            CompetitionVisibilityMutationState.NotFound => TypedResults.NotFound(),
            CompetitionVisibilityMutationState.InvalidSchedule => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Leaderboard visibility schedule is invalid.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = result.State
                }),
            CompetitionVisibilityMutationState.RevisionConflict
                or CompetitionVisibilityMutationState.CompetitionFinished => TypedResults.Conflict(
                    new CompetitionLeaderboardVisibilityFailureResponse(result.State, current)),
            _ => throw new ArgumentOutOfRangeException(nameof(result.State), result.State, null)
        };
    }
}
