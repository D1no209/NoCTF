using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionStatusRequest
{
    public CompetitionStatusProtocol? Status { get; set; }
}

public sealed class UpdateCompetitionStatusValidator
    : Validator<UpdateCompetitionStatusRequest>
{
    public UpdateCompetitionStatusValidator() =>
        RuleFor(request => request.Status)
            .NotNull()
            .IsInEnum()
            .Must(status => status != CompetitionStatusProtocol.Draft)
            .WithMessage(_ => ApiMessages.Text(ApiMessageId.UpdateCompetitionStatusValidationDraftUsedLifecycleTransition)).WithErrorCode(ApiMessages.Key(ApiMessageId.UpdateCompetitionStatusValidationDraftUsedLifecycleTransition));
}

public sealed class UpdateCompetitionStatusEndpoint(
    TransitionCompetitionLifecycle transition,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<UpdateCompetitionStatusRequest,
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/status");
        AuthSchemes("Bearer");
        Description(builder => builder
            .WithName("AdminUpdateCompetitionStatus")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Updates a competition lifecycle status.";
            summary.Description =
                "Applies the documented lifecycle state machine and is idempotent when the requested status is already current.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        UpdateCompetitionStatusRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var target = request.Status!.Value switch
        {
            CompetitionStatusProtocol.Visible => CompetitionStatus.Visible,
            CompetitionStatusProtocol.Published => CompetitionStatus.Published,
            CompetitionStatusProtocol.Running => CompetitionStatus.Running,
            CompetitionStatusProtocol.Paused => CompetitionStatus.Paused,
            CompetitionStatusProtocol.Finished => CompetitionStatus.Finished,
            _ => throw new ArgumentOutOfRangeException(nameof(request.Status))
        };
        var result = await transition.ExecuteAsync(
            competitionId,
            target,
            user.UserId,
            $"manual_{target.ToString().ToLowerInvariant()}",
            ct);
        if (result.FailureCode == CompetitionTransitionFailureCode.CompetitionNotFound)
            return TypedResults.NotFound();
        return result.Succeeded
            ? TypedResults.NoContent()
            : ApiProblems.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: ApiMessages.Get(ApiMessageId.UpdateCompetitionStatusTitleCompetitionStatusUpdated),
                detail: ApiMessages.For(result.FailureCode),
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = result.FailureCode?.ToString()
                });
    }
}
