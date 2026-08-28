using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Appeals;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class UpholdTeamBanAppealRequest
{
    public Guid CompetitionId { get; set; }
    public Guid AppealId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class UpholdTeamBanAppealValidator
    : Validator<UpholdTeamBanAppealRequest>
{
    public UpholdTeamBanAppealValidator() =>
        RuleFor(request => request.Reason).MinimumLength(8).MaximumLength(512);
}

public sealed class UpholdTeamBanAppealEndpoint(
    ResolveTeamBanAppeal resolve,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<UpholdTeamBanAppealRequest,
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/team-ban-appeals/{appealId}/uphold");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminUpholdTeamBanAppeal")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Upholds a team ban appeal privately.";
            summary.Description =
                "Records a terminal private decision without publishing a correction.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
            UpholdTeamBanAppealRequest request,
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
            TeamBanAppealResolution.Uphold,
            request.Reason,
            timeProvider.GetUtcNow()), cancellationToken);
        return TeamBanAppealHttpResults.Map(result, "Team ban appeal rejection was rejected.");
    }
}
