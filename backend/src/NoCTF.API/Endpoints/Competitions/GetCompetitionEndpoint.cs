using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Competitions;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<GameModeProtocol>))]
public enum GameModeProtocol
{
    Ctf,
    Awd,
    Awdp,
    Koh
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionStatusProtocol>))]
public enum CompetitionStatusProtocol
{
    Draft,
    Visible,
    Published,
    Running,
    Paused,
    Finished
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LeaderboardVisibilityProtocol>))]
public enum LeaderboardVisibilityProtocol
{
    Normal,
    Frozen,
    Blackout
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionAdministrationRoleProtocol>))]
public enum CompetitionAdministrationRoleProtocol
{
    Owner,
    Manager,
    Judge,
    Observer
}

[Mapper]
public static partial class CompetitionProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial GameMode ToDomain(GameModeProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial GameModeProtocol ToProtocol(GameMode value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial CompetitionStatusProtocol ToProtocol(CompetitionStatus value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial LeaderboardVisibilityProtocol ToProtocol(
        CompetitionLeaderboardVisibility value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial CompetitionLeaderboardVisibility ToDomain(
        LeaderboardVisibilityProtocol value);
}

public sealed record CompetitionResponse(
    Guid Id,
    string Title,
    string? Description,
    GameModeProtocol Mode,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    CompetitionStatusProtocol Status,
    bool TeamRegistrationAutoApprove,
    bool AllowTeamRegistrationWhileRunning,
    int MaxTeamMembers,
    int MaxConcurrentRuntimeInstancesPerTeam,
    Guid OwnerId,
    LeaderboardVisibilityProtocol LeaderboardVisibility,
    DateTimeOffset? DeletedAt,
    CompetitionAdministrationRoleProtocol? AdministrationRole = null,
    int MaxActiveQuestionsPerTeam = 5,
    int MaxParticipantMessagesBeforeHandlerReply = 3,
    bool AllowChallengeOwnersToHandleQuestions = true,
    bool PracticeModeEnabled = false);

internal static class CompetitionMapper
{
    public static CompetitionResponse ToResponse(
        CompetitionView view,
        DateTimeOffset now) =>
        new(
            view.Id,
            view.Title,
            view.Description,
            CompetitionProtocolMapper.ToProtocol(view.Mode),
            view.StartTime,
            view.EndTime,
            CompetitionProtocolMapper.ToProtocol(view.Status),
            view.TeamRegistrationAutoApprove,
            view.AllowTeamRegistrationWhileRunning,
            view.MaxTeamMembers,
            view.MaxConcurrentRuntimeInstancesPerTeam,
            view.OwnerId,
            CompetitionProtocolMapper.ToProtocol(
                CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
                    view.FrozenStartAt,
                    view.HiddenStartAt,
                    now)),
            view.DeletedAt,
            AdministrationRole: null,
            view.MaxActiveQuestionsPerTeam,
            view.MaxParticipantMessagesBeforeHandlerReply,
            view.AllowChallengeOwnersToHandleQuestions,
            view.PracticeModeEnabled);
}

internal static class CompetitionAdministrationRoleResolver
{
    public static async Task<CompetitionAdministrationRoleProtocol?> ResolveAsync(
        CompetitionView view,
        IUserContext user,
        ICompetitionModerationAuthorizer authorizer,
        CancellationToken cancellationToken,
        bool accessAlreadyEstablished = false)
    {
        if (user.UserId == Guid.Empty)
            return null;
        if (user.IsAdministrator || view.OwnerId == user.UserId)
            return CompetitionAdministrationRoleProtocol.Owner;
        if (!accessAlreadyEstablished
            && !await authorizer.CanObserveAsync(user.UserId, view.Id, cancellationToken))
            return null;
        if (await authorizer.CanModerateAsync(user.UserId, view.Id, cancellationToken))
            return CompetitionAdministrationRoleProtocol.Manager;
        if (await authorizer.CanJudgeAsync(user.UserId, view.Id, cancellationToken))
            return CompetitionAdministrationRoleProtocol.Judge;
        return CompetitionAdministrationRoleProtocol.Observer;
    }
}

public sealed class GetCompetitionRequest { public Guid CompetitionId { get; set; } }

public sealed class GetCompetitionEndpoint(
    GetCompetition get,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<GetCompetitionRequest, Results<Ok<CompetitionResponse>, NotFound>>
{
    public override void Configure() { Get("/competitions/{competitionId}"); AllowAnonymous(); }

    public override async Task<Results<Ok<CompetitionResponse>, NotFound>> ExecuteAsync(GetCompetitionRequest request, CancellationToken ct)
    {
        var view = await get.ExecuteAsync(Route<Guid>("competitionId"), false, ct);
        if (view is null)
            return TypedResults.NotFound();

        var role = await CompetitionAdministrationRoleResolver.ResolveAsync(view, user, authorizer, ct);
        return TypedResults.Ok(CompetitionMapper.ToResponse(
            view,
            timeProvider.GetUtcNow()) with { AdministrationRole = role });
    }
}
