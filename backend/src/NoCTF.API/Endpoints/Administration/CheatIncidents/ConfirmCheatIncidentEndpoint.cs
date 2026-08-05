using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Submissions.CheatIncidents;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.CheatIncidents;

public sealed class ConfirmCheatIncidentRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class ConfirmCheatIncidentValidator : Validator<ConfirmCheatIncidentRequest>
{
    public ConfirmCheatIncidentValidator() =>
        RuleFor(request => request.Reason).NotEmpty().MinimumLength(8).MaximumLength(512);
}

public sealed class ConfirmCheatIncidentEndpoint(
    ResolveCheatIncident resolve,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<ConfirmCheatIncidentRequest,
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/cheat-incidents/{scoringEventId}/confirm");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminConfirmCheatIncident"));
        Summary(summary =>
        {
            summary.Summary = "Confirms one incident and bans its source team.";
            summary.Description =
                "Administrator, owner, and manager only. The public competition notification contains no Flag or private evidence.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
            ConfirmCheatIncidentRequest request,
            CancellationToken cancellationToken)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, cancellationToken))
            return TypedResults.Forbid();
        var result = await resolve.ConfirmAndBanAsync(new(
            competitionId,
            Route<Guid>("scoringEventId"),
            user.UserId,
            request.Reason,
            timeProvider.GetUtcNow()), cancellationToken);
        return CheatIncidentResolutionHttpResults.Map(result, "Incident confirmation was rejected.");
    }
}
