using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Appeals;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class AcceptTeamBanAppealRequest
{
    public Guid CompetitionId { get; set; }
    public Guid AppealId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class AcceptTeamBanAppealValidator
    : Validator<AcceptTeamBanAppealRequest>
{
    public AcceptTeamBanAppealValidator() =>
        RuleFor(request => request.Reason).MinimumLength(8).MaximumLength(512);
}

public sealed class AcceptTeamBanAppealEndpoint(
    ResolveTeamBanAppeal resolve,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<AcceptTeamBanAppealRequest,
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/team-ban-appeals/{appealId}/accept");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminAcceptTeamBanAppeal")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Accepts a private team ban appeal.";
            summary.Description =
                "Corrects the ban, restores historical scoring eligibility, and publishes a generic correction.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
            AcceptTeamBanAppealRequest request,
            CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.AppealId = Route<Guid>("appealId");
        if (!await authorizer.CanJudgeAsync(
                user.UserId,
                request.CompetitionId,
                cancellationToken))
        {
            return TypedResults.Forbid();
        }
        var result = await resolve.ExecuteAsync(new(
            request.CompetitionId,
            request.AppealId,
            user.UserId,
            TeamBanAppealResolution.Accept,
            request.Reason,
            DateTimeOffset.UtcNow), cancellationToken);
        return TeamBanAppealHttpResults.Map(result, "Team ban appeal acceptance was rejected.");
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
