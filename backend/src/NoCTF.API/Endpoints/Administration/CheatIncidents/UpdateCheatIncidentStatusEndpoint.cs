using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.CheatIncidents;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.CheatIncidents;

public sealed class UpdateCheatIncidentStatusRequest
{
    public CheatIncidentStatusProtocol? Status { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class UpdateCheatIncidentStatusValidator
    : Validator<UpdateCheatIncidentStatusRequest>
{
    public UpdateCheatIncidentStatusValidator()
    {
        RuleFor(request => request.Status)
            .NotNull()
            .IsInEnum()
            .Must(status => status is CheatIncidentStatusProtocol.Confirmed
                or CheatIncidentStatusProtocol.Dismissed
                or CheatIncidentStatusProtocol.Corrected)
            .WithMessage(_ => ApiMessages.Text(ApiMessageId.UpdateCheatIncidentStatusValidationStatusConfirmedDismissedCorrected)).WithErrorCode(ApiMessages.Key(ApiMessageId.UpdateCheatIncidentStatusValidationStatusConfirmedDismissedCorrected));
        RuleFor(request => request.Reason).NotEmpty().MinimumLength(8).MaximumLength(512);
    }
}

public sealed class UpdateCheatIncidentStatusEndpoint(
    ResolveCheatIncident resolve,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<UpdateCheatIncidentStatusRequest,
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/cheat-incidents/{gameplayFactId}/status");
        AuthSchemes("Bearer");
        Description(builder => builder
            .WithName("AdminUpdateCheatIncidentStatus")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary => { summary.Summary = "Updates a cheat incident status."; summary.Description = summary.Summary; });
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        UpdateCheatIncidentStatusRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var canUpdate = request.Status == CheatIncidentStatusProtocol.Corrected
            ? await authorizer.CanModerateAsync(user.UserId, competitionId, ct)
            : await authorizer.CanJudgeAsync(user.UserId, competitionId, ct);
        if (!canUpdate)
            return TypedResults.Forbid();

        var command = new CheatIncidentResolutionCommand(
            competitionId,
            Route<Guid>("gameplayFactId"),
            user.UserId,
            request.Reason,
            timeProvider.GetUtcNow());
        var result = request.Status switch
        {
            CheatIncidentStatusProtocol.Confirmed =>
                await resolve.ConfirmAndBanAsync(command, ct),
            CheatIncidentStatusProtocol.Dismissed =>
                await resolve.DismissAsync(command, ct),
            CheatIncidentStatusProtocol.Corrected =>
                await resolve.CorrectAndUnbanAsync(command, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Status))
        };
        return CheatIncidentResolutionHttpResults.Map(
            result,
            "Cheat incident status update was rejected.");
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
        return ApiProblems.Problem(
            statusCode: statusCode,
            title: ApiMessages.For(result.Failure),
            detail: ApiMessages.For(result.Failure),
            extensions: new Dictionary<string, object?>
            {
                ["code"] = result.Failure.ToString()
            });
    }
}
