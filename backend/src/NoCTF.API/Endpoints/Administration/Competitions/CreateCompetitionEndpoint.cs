using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Competitions;

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionResourceManagerConflictCode>))]
public enum CompetitionResourceManagerConflictCode
{
    RolesOverlap,
    OwnerIncluded,
    UserNotFound,
    RoleNotEligible,
    EmailNotVerified
}

public sealed record CompetitionResourceManagerConflictResponse(
    CompetitionResourceManagerConflictCode Code,
    string Detail,
    IReadOnlyList<Guid> UserIds)
{
    public string Detail { get; init; } = ApiMessages.Localize(Code, Detail, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

internal static class CompetitionResourceManagerConflictMapper
{
    public static CompetitionResourceManagerConflictResponse ToResponse(
        CompetitionResourceManagerConflictCode code,
        IReadOnlyList<Guid>? userIds) =>
        new(code, code.ToString(), userIds ?? []);
}

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
    public bool TracksEnabled { get; set; }
    public CompetitionAccessModeProtocol AccessMode { get; set; } =
        CompetitionAccessModeProtocol.Public;
}

public sealed class CreateCompetitionValidator : Validator<CreateCompetitionRequest>
{
    public CreateCompetitionValidator()
    {
        RuleFor(request => request.Title).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Mode).IsInEnum();
        RuleFor(request => request.AccessMode).IsInEnum();
        RuleFor(request => request.EndTime).GreaterThan(request => request.StartTime);
        RuleFor(request => request.MaxTeamMembers).GreaterThan(0);
        RuleFor(request => request.MaxConcurrentRuntimeInstancesPerTeam).NotNull();
        RuleFor(request => request.MaxActiveQuestionsPerTeam).GreaterThan(0);
        RuleFor(request => request.MaxParticipantMessagesBeforeHandlerReply).GreaterThan(0);
    }
}

public sealed class CreateCompetitionEndpoint(CreateCompetition create, IUserContext user, TimeProvider timeProvider)
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
            timeProvider.GetUtcNow(),
            request.AllowTeamRegistrationWhileRunning,
            request.MaxActiveQuestionsPerTeam,
            request.MaxParticipantMessagesBeforeHandlerReply,
            request.AllowChallengeOwnersToHandleQuestions,
            request.PracticeModeEnabled,
            request.TracksEnabled,
            CompetitionProtocolMapper.ToDomain(request.AccessMode)), ct);
        return result.State switch
        {
            CompetitionCreationState.Created =>
                TypedResults.Created(
                    $"/api/v1/admin/competitions/{result.Competition!.Id}",
                    CompetitionMapper.ToResponse(result.Competition, timeProvider.GetUtcNow())),
            CompetitionCreationState.InvalidRequest =>
                ApiProblems.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: ApiMessages.Get(ApiMessageId.CreateCompetitionTitleCompetitionWasCreated),
                    detail: ApiMessages.Get(ApiMessageId.CreateCompetitionTitleCompetitionWasCreated)),
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
