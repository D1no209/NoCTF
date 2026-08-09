using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public bool TeamRegistrationAutoApprove { get; set; }
    public bool AllowTeamRegistrationWhileRunning { get; set; }
    public int MaxTeamMembers { get; set; }
    public int? MaxConcurrentRuntimeInstancesPerTeam { get; set; }
    public int MaxActiveQuestionsPerTeam { get; set; } = 5;
    public int MaxParticipantMessagesBeforeHandlerReply { get; set; } = 3;
    public bool AllowChallengeOwnersToHandleQuestions { get; set; } = true;
}

public sealed class UpdateCompetitionValidator : Validator<UpdateCompetitionRequest>
{
    public UpdateCompetitionValidator()
    {
        RuleFor(request => request.Title).NotEmpty().MaximumLength(160);
        RuleFor(request => request.EndTime).GreaterThan(request => request.StartTime);
        RuleFor(request => request.MaxTeamMembers).GreaterThan(0);
        RuleFor(request => request.MaxConcurrentRuntimeInstancesPerTeam).NotNull();
        RuleFor(request => request.MaxActiveQuestionsPerTeam).GreaterThan(0);
        RuleFor(request => request.MaxParticipantMessagesBeforeHandlerReply).GreaterThan(0);
    }
}

public sealed class UpdateCompetitionEndpoint(
    UpdateCompetition update,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<UpdateCompetitionRequest,
        Results<Ok<CompetitionResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminUpdateCompetition")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Updates competition metadata.";
            summary.Description = "Updates mutable competition metadata using the current lifecycle state as the concurrency fence.";
        });
    }

    public override async Task<
        Results<Ok<CompetitionResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        UpdateCompetitionRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var result = await update.ExecuteAsync(new UpdateCompetitionCommand(
            competitionId,
            request.Title,
            request.Description,
            request.StartTime,
            request.EndTime,
            request.TeamRegistrationAutoApprove,
            request.MaxTeamMembers,
            request.MaxConcurrentRuntimeInstancesPerTeam!.Value,
            user.UserId,
            DateTimeOffset.UtcNow,
            request.AllowTeamRegistrationWhileRunning,
            request.MaxActiveQuestionsPerTeam,
            request.MaxParticipantMessagesBeforeHandlerReply,
            request.AllowChallengeOwnersToHandleQuestions), ct);
        if (result.FailureCode == CompetitionManagementFailureCode.CompetitionNotFound)
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            return TypedResults.Problem(
                statusCode: result.FailureCode == CompetitionManagementFailureCode.CompetitionConflict
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest,
                title: "Competition was not updated.",
                detail: result.ErrorMessage);
        }

        return TypedResults.Ok(CompetitionMapper.ToResponse(result.Value!));
    }
}
