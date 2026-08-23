using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionLeaderboardVisibilityRequest
{
    public LeaderboardVisibilityProtocol? Visibility { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public string? Reason { get; set; }
}

public sealed class UpdateCompetitionLeaderboardVisibilityValidator
    : Validator<UpdateCompetitionLeaderboardVisibilityRequest>
{
    public UpdateCompetitionLeaderboardVisibilityValidator()
    {
        RuleFor(request => request.Visibility).NotNull().IsInEnum();
        RuleFor(request => request.Reason).MaximumLength(500);
    }
}

 [JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionVisibilityMutationCodeProtocol>))]
public enum CompetitionVisibilityMutationCodeProtocol
{
    Updated,
    NotFound,
    InvalidSchedule,
    CompetitionFinished
}

public sealed record CompetitionLeaderboardVisibilityFailureResponse(
    CompetitionVisibilityMutationCodeProtocol Code,
    CompetitionLeaderboardVisibilityResponse? Current);

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Source)]
internal static partial class CompetitionVisibilityMutationMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial CompetitionVisibilityMutationCodeProtocol ToProtocol(
        CompetitionVisibilityMutationState value);
}

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
            summary.Description = "Persists the exact Frozen cutoff snapshot when the restriction takes effect.";
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
            CompetitionProtocolMapper.ToDomain(request.Visibility!.Value),
            request.StartsAt,
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
                    ["code"] = CompetitionVisibilityMutationMapper.ToProtocol(result.State)
                }),
            CompetitionVisibilityMutationState.CompetitionFinished => TypedResults.Conflict(
                    new CompetitionLeaderboardVisibilityFailureResponse(
                        CompetitionVisibilityMutationMapper.ToProtocol(result.State),
                        current)),
            _ => throw new ArgumentOutOfRangeException(nameof(result.State), result.State, null)
        };
    }
}
