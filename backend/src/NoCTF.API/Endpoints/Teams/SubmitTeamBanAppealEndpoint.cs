using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Appeals;

namespace NoCTF.API.Endpoints.Teams;

public sealed class SubmitTeamBanAppealRequest
{
    public Guid CompetitionId { get; set; }
    public string Statement { get; set; } = string.Empty;
}

public sealed class SubmitTeamBanAppealValidator
    : Validator<SubmitTeamBanAppealRequest>
{
    public SubmitTeamBanAppealValidator() =>
        RuleFor(request => request.Statement).MinimumLength(16).MaximumLength(512);
}

public sealed class SubmitTeamBanAppealEndpoint(
    SubmitTeamBanAppeal submit,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<SubmitTeamBanAppealRequest,
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/team-ban-appeals");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("SubmitTeamBanAppeal")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Submits one private appeal for the current team ban.";
            summary.Description =
                "Only the captain may submit, and each immutable ban event accepts at most one appeal.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
            SubmitTeamBanAppealRequest request,
            CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var result = await submit.ExecuteAsync(new(
            request.CompetitionId,
            user.UserId,
            request.Statement,
            timeProvider.GetUtcNow()), cancellationToken);
        if (result.Succeeded)
            return TypedResults.NoContent();
        if (result.Failure == TeamBanAppealFailure.CaptainRequired)
            return TypedResults.Forbid();
        if (result.Failure is TeamBanAppealFailure.CompetitionNotFound
            or TeamBanAppealFailure.TeamNotFound
            or TeamBanAppealFailure.BanNotFound)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Problem(
            statusCode: result.Failure == TeamBanAppealFailure.AppealAlreadySubmitted
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest,
            title: "Team ban appeal was rejected.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = result.Failure?.ToString()
            });
    }
}
