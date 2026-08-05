using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Appeals;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class CorrectTeamBanRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class CorrectTeamBanValidator : Validator<CorrectTeamBanRequest>
{
    public CorrectTeamBanValidator() =>
        RuleFor(request => request.Reason).MinimumLength(8).MaximumLength(512);
}

public sealed class CorrectTeamBanEndpoint(
    CorrectTeamBan correct,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<CorrectTeamBanRequest,
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/teams/{teamId}/correct-ban");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCorrectTeamBan")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Corrects the current team ban without requiring an appeal.";
            summary.Description =
                "Available after competition finish; it restores the historical projection and publishes only a generic correction.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
            CorrectTeamBanRequest request,
            CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.TeamId = Route<Guid>("teamId");
        if (!await authorizer.CanModerateAsync(
                user.UserId,
                request.CompetitionId,
                cancellationToken))
        {
            return TypedResults.Forbid();
        }
        var result = await correct.ExecuteAsync(new(
            request.CompetitionId,
            request.TeamId,
            user.UserId,
            request.Reason,
            DateTimeOffset.UtcNow), cancellationToken);
        return TeamBanAppealHttpResults.Map(result, "Team ban correction was rejected.");
    }
}
