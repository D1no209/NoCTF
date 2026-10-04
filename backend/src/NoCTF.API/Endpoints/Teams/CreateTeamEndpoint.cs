using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Security;
using NoCTF.API.Pagination;
using NoCTF.API.Serialization;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Teams;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Teams;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<TeamRegistrationStatusProtocol>))]
public enum TeamRegistrationStatusProtocol
{
    Pending,
    Approved,
    Rejected,
    Unregistered
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<TeamBanAppealStatusProtocol>))]
public enum TeamBanAppealStatusProtocol
{
    Submitted,
    Upheld,
    Accepted
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<TeamBanSourceProtocol>))]
public enum TeamBanSourceProtocol
{
    ManualModeration,
    CheatIncident
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<TeamRegistrationFailureCodeProtocol>))]
public enum TeamRegistrationFailureCodeProtocol
{
    InvalidTeamName,
    CompetitionNotFound,
    RegistrationClosed,
    UserAlreadyRegistered,
    TeamNameOrMembershipConflict,
    TeamNotFound,
    CompetitionFinished,
    TeamLocked,
    TeamBanned,
    TeamConflict,
    TeamReviewConflict,
    CompetitionActive,
    TrackNotFound,
    TrackNotPublicSelectable,
    TrackInvitationRequired,
    TrackInvitationInvalid,
    TrackSsoIdentityRequired
}

public sealed record TeamRegistrationFailureResponse(
    TeamRegistrationFailureCodeProtocol Code,
    string Message)
{
    public string Message { get; init; } = ApiMessages.Localize(Code, Message, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed class CreateTeamRequest
{
    private string name = string.Empty;

    public Guid CompetitionId { get; set; }
    public string? TrackKey { get; set; }
    public string? TrackInvitationCode { get; set; }
    public string Name
    {
        get => name;
        set => name = value?.Trim() ?? string.Empty;
    }
}

public sealed record TeamResponse(
    Guid Id,
    Guid CompetitionId,
    string TrackKey,
    string TrackName,
    string Name,
    string? AvatarUrl,
    Guid CaptainId,
    IReadOnlyList<Guid> MemberIds,
    TeamRegistrationStatusProtocol RegistrationStatus,
    bool IsLocked,
    bool IsBanned,
    DateTimeOffset RegisteredAt);

public sealed class TeamListResponse : ArrayResult<TeamResponse>
{
    public TeamListResponse() { }

    public TeamListResponse(TeamResponse[] items)
        : base(items, items.Length) { }

    public TeamListResponse(TeamResponse[] items, int total)
        : base(items, total) { }
}

[Mapper]
internal static partial class TeamMapper
{
    public static partial CreateTeamCommand ToCommand(
        CreateTeamRequest request,
        Guid userId,
        DateTimeOffset registeredAt);
    public static TeamResponse ToResponse(
        TeamView view,
        LinkGenerator links,
        HttpContext httpContext)
    {
        string? avatarUrl = null;
        if (view.AvatarFileId is not null)
        {
            var path = links.GetPathByName(
                httpContext,
                "TeamAvatar_Get",
                new { competitionId = view.CompetitionId, teamId = view.Id });
            if (path is not null)
                avatarUrl = $"{path}?revision={view.AvatarFileId.Value:N}";
        }

        return new(
            view.Id,
            view.CompetitionId,
            view.TrackKey,
            view.TrackName,
            view.Name,
            avatarUrl,
            view.CaptainId,
            view.MemberIds,
            ToProtocol(view.RegistrationStatus),
            view.IsLocked,
            view.IsBanned,
            view.RegisteredAt);
    }

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial TeamRegistrationStatusProtocol ToProtocol(
        TeamRegistrationStatus value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial TeamBanAppealStatusProtocol ToProtocol(
        TeamBanAppealStatus value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial TeamBanSourceProtocol ToProtocol(TeamBanSource value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial TeamRegistrationFailureCodeProtocol ToProtocol(
        TeamRegistrationFailure value);
}

public sealed class CreateTeamEndpoint(CreateTeam create, IUserContext user, LinkGenerator links, TimeProvider timeProvider)
    : Endpoint<CreateTeamRequest, Results<Created<TeamResponse>, NotFound, Conflict<TeamRegistrationFailureResponse>>>
{
    public override void Configure() { Post("/competitions/{competitionId}/teams"); AuthSchemes("Bearer"); }
    public override async Task<Results<Created<TeamResponse>, NotFound, Conflict<TeamRegistrationFailureResponse>>> ExecuteAsync(CreateTeamRequest request, CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var result = await create.ExecuteAsync(TeamMapper.ToCommand(request, user.UserId, timeProvider.GetUtcNow()), ct);
        if (result.FailureCode == TeamRegistrationFailure.CompetitionNotFound) return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.Conflict(new TeamRegistrationFailureResponse(
                TeamMapper.ToProtocol(result.FailureCode!.Value),
                result.ErrorMessage ?? "Team was not created."));
        var response = TeamMapper.ToResponse(result.Value!, links, HttpContext);
        return TypedResults.Created($"/competitions/{request.CompetitionId}/teams/{response.Id}", response);
    }
}
