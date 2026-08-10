using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.CheatIncidents;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.CheatIncidents;

public sealed class CorrectCheatIncidentRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class CorrectCheatIncidentValidator : Validator<CorrectCheatIncidentRequest>
{
    public CorrectCheatIncidentValidator() =>
        RuleFor(request => request.Reason).NotEmpty().MinimumLength(8).MaximumLength(512);
}

public sealed class CorrectCheatIncidentEndpoint(
    ResolveCheatIncident resolve,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<CorrectCheatIncidentRequest,
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/cheat-incidents/{gameplayFactId}/correct");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCorrectCheatIncident"));
        Summary(summary =>
        {
            summary.Summary = "Corrects a confirmed incident and unbans its source team.";
            summary.Description =
                "Administrator, owner, and manager only. A safe correction notice is delivered to the whole competition.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
            CorrectCheatIncidentRequest request,
            CancellationToken cancellationToken)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, cancellationToken))
            return TypedResults.Forbid();
        var result = await resolve.CorrectAndUnbanAsync(new(
            competitionId,
            Route<Guid>("gameplayFactId"),
            user.UserId,
            request.Reason,
            timeProvider.GetUtcNow()), cancellationToken);
        return CheatIncidentResolutionHttpResults.Map(result, "Incident correction was rejected.");
    }
}

internal static class CheatIncidentResolutionHttpResults
{
    public static Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult> Map(
        CheatIncidentResolutionResult result,
        string title)
    {
        if (result.Succeeded)
            return TypedResults.NoContent();
        if (result.Failure == CheatIncidentResolutionFailure.NotFound)
            return TypedResults.NotFound();
        var statusCode = result.Failure is
            CheatIncidentResolutionFailure.CompetitionFinished
            or CheatIncidentResolutionFailure.NotPending
            or CheatIncidentResolutionFailure.AlreadyBanned
            or CheatIncidentResolutionFailure.NotConfirmed
            or CheatIncidentResolutionFailure.TeamNotBanned
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
        return TypedResults.Problem(
            statusCode: statusCode,
            title: title,
            detail: result.Failure.ToString());
    }
}
