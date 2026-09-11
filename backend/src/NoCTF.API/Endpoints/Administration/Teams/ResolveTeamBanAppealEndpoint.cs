using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Teams.Appeals;
using NoCTF.Application.Teams.Moderation;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Teams;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<TeamBanAppealResolutionProtocol>))]
public enum TeamBanAppealResolutionProtocol
{
    Accepted,
    Upheld
}

public sealed class ResolveTeamBanAppealRequest
{
    public TeamBanAppealResolutionProtocol? Resolution { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class ResolveTeamBanAppealValidator
    : Validator<ResolveTeamBanAppealRequest>
{
    public ResolveTeamBanAppealValidator()
    {
        RuleFor(request => request.Resolution).NotNull().IsInEnum();
        RuleFor(request => request.Reason).MinimumLength(8).MaximumLength(512);
    }
}

public sealed class ResolveTeamBanAppealEndpoint(
    ResolveTeamBanAppeal resolve,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<ResolveTeamBanAppealRequest,
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/team-ban-appeals/{appealId}/resolution");
        AuthSchemes("Bearer");
        Description(builder => builder
            .WithName("AdminResolveTeamBanAppeal")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary => summary.Summary = "Resolves a private team-ban appeal.");
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        ResolveTeamBanAppealRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var result = await resolve.ExecuteAsync(new(
            competitionId,
            Route<Guid>("appealId"),
            user.UserId,
            request.Resolution == TeamBanAppealResolutionProtocol.Accepted
                ? TeamBanAppealResolution.Accept
                : TeamBanAppealResolution.Uphold,
            request.Reason,
            timeProvider.GetUtcNow()), ct);
        return TeamBanAppealHttpResults.Map(result, "Team-ban appeal resolution was rejected.");
    }
}

internal static class TeamBanAppealHttpResults
{
    internal static Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult> Map(
        TeamBanAppealMutationResult result,
        string title)
    {
        if (result.Succeeded)
            return TypedResults.NoContent();
        if (result.Failure is TeamBanAppealFailure.CompetitionNotFound
            or TeamBanAppealFailure.TeamNotFound
            or TeamBanAppealFailure.BanNotFound
            or TeamBanAppealFailure.AppealNotFound)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Problem(
            statusCode: result.Failure is TeamBanAppealFailure.AppealAlreadyResolved
                or TeamBanAppealFailure.BanNoLongerCurrent
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest,
            title: title,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = result.Failure?.ToString()
            });
    }
}
