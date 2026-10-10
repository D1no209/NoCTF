using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Competitions;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<GameModeProtocol>))]
public enum GameModeProtocol
{
    Ctf,
    Awd,
    Awdp,
    Koh,
    LiveSolo
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

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionAccessModeProtocol>))]
public enum CompetitionAccessModeProtocol
{
    Public,
    StaffOnly
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeAccessModeProtocol>))]
public enum RuntimeAccessModeProtocol
{
    Direct,
    DirectAndWsrx,
    WsrxOnly
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
    public static partial CompetitionAccessModeProtocol ToProtocol(CompetitionAccessMode value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial CompetitionAccessMode ToDomain(CompetitionAccessModeProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RuntimeAccessModeProtocol ToProtocol(RuntimeAccessMode value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RuntimeAccessMode ToDomain(RuntimeAccessModeProtocol value);

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
    string? PosterUrl,
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
    bool PracticeModeEnabled = false,
    bool TracksEnabled = false,
    CompetitionAccessModeProtocol AccessMode = CompetitionAccessModeProtocol.Public,
    bool WriteUpSubmissionRequired = false,
    int WriteUpSubmissionDeadlineHours = 0,
    DateTimeOffset? WriteUpSubmissionDeadlineAt = null,
    RuntimeAccessModeProtocol RuntimeAccessMode = RuntimeAccessModeProtocol.Direct,
    bool TrafficCaptureEnabled = false,
    long? TrafficCaptureLimitBytes = null,
    bool SingleWriteUpsEnabled = false,
    int SingleWriteUpDeductionPercent = 20,
    int SingleWriteUpDeadlineHours = 24,
    DateTimeOffset? SingleWriteUpDeadlineAt = null);

internal static class CompetitionMapper
{
    public static CompetitionResponse ToResponse(
        CompetitionView view,
        DateTimeOffset now) =>
        new(
            view.Id,
            view.Title,
            view.Description,
            view.PosterFileId is { } posterFileId
                ? PosterUrl(view.Id, posterFileId)
                : null,
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
            view.PracticeModeEnabled,
            view.TracksEnabled,
            CompetitionProtocolMapper.ToProtocol(view.AccessMode),
            view.WriteUpSubmissionRequired,
            view.WriteUpSubmissionDeadlineHours,
            CompetitionWriteUpPolicy.DeadlineAt(
                view.EndTime,
                view.WriteUpSubmissionDeadlineHours),
            CompetitionProtocolMapper.ToProtocol(view.RuntimeAccessMode),
            view.TrafficCaptureEnabled,
            view.TrafficCaptureLimitBytes, view.SingleWriteUpsEnabled, view.SingleWriteUpDeductionPercent, view.SingleWriteUpDeadlineHours,
            CompetitionWriteUpPolicy.DeadlineAt(view.EndTime, view.SingleWriteUpDeadlineHours));

    internal static string PosterUrl(Guid competitionId, Guid posterFileId) =>
        $"/api/v1/competitions/{competitionId}/poster?revision={posterFileId:N}";
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
        var competitionId = Route<Guid>("competitionId");
        var staffAccess = user.UserId != Guid.Empty
            && await authorizer.CanObserveAsync(user.UserId, competitionId, ct);
        var view = await get.ExecuteAsync(competitionId, staffAccess, ct);
        if (view is null)
            return TypedResults.NotFound();

        if (staffAccess)
        {
            HttpContext.Response.Headers.CacheControl = "private,no-store";
            HttpContext.Response.Headers.Vary = "Authorization";
        }
        var role = await CompetitionAdministrationRoleResolver.ResolveAsync(
            view, user, authorizer, ct, accessAlreadyEstablished: staffAccess);
        return TypedResults.Ok(CompetitionMapper.ToResponse(
            view,
            timeProvider.GetUtcNow()) with { AdministrationRole = role });
    }
}
