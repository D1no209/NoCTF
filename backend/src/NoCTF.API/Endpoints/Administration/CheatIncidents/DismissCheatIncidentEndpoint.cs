using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.CheatIncidents;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.CheatIncidents;

public sealed class DismissCheatIncidentRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class DismissCheatIncidentValidator : Validator<DismissCheatIncidentRequest>
{
    public DismissCheatIncidentValidator() =>
        RuleFor(request => request.Reason).NotEmpty().MinimumLength(8).MaximumLength(512);
}

public sealed class DismissCheatIncidentEndpoint(
    ResolveCheatIncident resolve,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<DismissCheatIncidentRequest,
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/cheat-incidents/{gameplayFactId}/dismiss");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDismissCheatIncident"));
        Summary(summary =>
        {
            summary.Summary = "Dismisses one pending cheat incident with an audit reason.";
            summary.Description =
                "Administrator, owner, manager, and judge only. Dismissal is silent and does not ban a team.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
            DismissCheatIncidentRequest request,
            CancellationToken cancellationToken)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, cancellationToken))
            return TypedResults.Forbid();
        var result = await resolve.DismissAsync(new(
            competitionId,
            Route<Guid>("gameplayFactId"),
            user.UserId,
            request.Reason,
            timeProvider.GetUtcNow()), cancellationToken);
        return CheatIncidentResolutionHttpResults.Map(result, "Incident dismissal was rejected.");
    }
}
