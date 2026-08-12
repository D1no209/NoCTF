using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class CreateCompetitionRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GameModeProtocol Mode { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public bool TeamRegistrationAutoApprove { get; set; } = true;
    public bool AllowTeamRegistrationWhileRunning { get; set; }
    public int MaxTeamMembers { get; set; } = 5;
    public int? MaxConcurrentRuntimeInstancesPerTeam { get; set; }
    public int MaxActiveQuestionsPerTeam { get; set; } = 5;
    public int MaxParticipantMessagesBeforeHandlerReply { get; set; } = 3;
    public bool AllowChallengeOwnersToHandleQuestions { get; set; } = true;
    public bool PracticeModeEnabled { get; set; }
}

public sealed class CreateCompetitionValidator : Validator<CreateCompetitionRequest>
{
    public CreateCompetitionValidator()
    {
        RuleFor(request => request.Title).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Mode).IsInEnum();
        RuleFor(request => request.EndTime).GreaterThan(request => request.StartTime);
        RuleFor(request => request.MaxTeamMembers).GreaterThan(0);
        RuleFor(request => request.MaxConcurrentRuntimeInstancesPerTeam).NotNull();
        RuleFor(request => request.MaxActiveQuestionsPerTeam).GreaterThan(0);
        RuleFor(request => request.MaxParticipantMessagesBeforeHandlerReply).GreaterThan(0);
    }
}

public sealed class CreateCompetitionEndpoint(CreateCompetition create, IUserContext user)
    : Endpoint<
        CreateCompetitionRequest,
        Results<
            Created<CompetitionResponse>,
            Conflict<CompetitionResourceManagerConflictResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions");
        AuthSchemes("Bearer");
        Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminCreateCompetition"));
        Summary(summary =>
        {
            summary.Summary = "Creates a competition.";
            summary.Description = "Creates a draft competition owned by the current organizer or administrator.";
        });
    }

    public override async Task<
        Results<
            Created<CompetitionResponse>,
            Conflict<CompetitionResourceManagerConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
        CreateCompetitionRequest request,
        CancellationToken ct)
    {
        var result = await create.ExecuteAsync(new CreateCompetitionCommand(
            request.Title,
            request.Description,
            CompetitionProtocolMapper.ToDomain(request.Mode),
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
            request.AllowChallengeOwnersToHandleQuestions,
            request.PracticeModeEnabled), ct);
        return result.State switch
        {
            CompetitionCreationState.Created =>
                TypedResults.Created(
                    $"/api/v1/admin/competitions/{result.Competition!.Id}",
                    CompetitionMapper.ToResponse(result.Competition)),
            CompetitionCreationState.InvalidRequest =>
                TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Competition was not created.",
                    detail: result.Detail),
            CompetitionCreationState.UserNotFound =>
                TypedResults.Conflict(
                    CompetitionResourceManagerConflictMapper.ToResponse(
                        CompetitionResourceManagerConflictCode.UserNotFound,
                        result.UserIds)),
            CompetitionCreationState.RoleNotEligible =>
                TypedResults.Conflict(
                    CompetitionResourceManagerConflictMapper.ToResponse(
                        CompetitionResourceManagerConflictCode.RoleNotEligible,
                        result.UserIds)),
            _ => throw new InvalidOperationException(
                $"Unsupported competition creation state: {result.State}.")
        };
    }
}
