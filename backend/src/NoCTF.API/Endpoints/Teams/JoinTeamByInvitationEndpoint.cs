using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Teams.Membership;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Teams;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<TeamMembershipFailureCodeProtocol>))]
public enum TeamMembershipFailureCodeProtocol
{
    CompetitionNotFound,
    TeamNotFound,
    TeamForbidden,
    TeamBanned,
    MembershipLocked,
    UserAlreadyRegistered,
    TeamFull,
    MembershipConflict,
    CaptainCannotBeRemoved,
    MemberNotFound,
    MembershipNotFound,
    CaptainMustTransfer,
    CaptainOnly
}

public sealed record TeamMembershipFailureResponse(
    TeamMembershipFailureCodeProtocol Code,
    string Message);

public sealed class JoinTeamByInvitationRequest
{
    public Guid CompetitionId { get; set; }
    public string InvitationToken { get; set; } = string.Empty;
}

public sealed class JoinTeamByInvitationValidator : Validator<JoinTeamByInvitationRequest>
{
    public JoinTeamByInvitationValidator() =>
        RuleFor(request => request.InvitationToken).NotEmpty().Length(32);
}

[Mapper]
internal static partial class TeamMembershipMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial TeamMembershipFailureCodeProtocol ToProtocol(
        TeamMembershipFailure value);
}

public sealed class JoinTeamByInvitationEndpoint(JoinTeamByInvitation join, IUserContext user, TimeProvider timeProvider)
    : Endpoint<JoinTeamByInvitationRequest, Results<NoContent, Conflict<TeamMembershipFailureResponse>>>
{
    public override void Configure() { Post("/competitions/{competitionId}/teams/join"); AuthSchemes("Bearer"); }
    public override async Task<Results<NoContent, Conflict<TeamMembershipFailureResponse>>> ExecuteAsync(
        JoinTeamByInvitationRequest request,
        CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var result = await join.ExecuteAsync(
            request.CompetitionId,
            request.InvitationToken,
            user.UserId,
            timeProvider.GetUtcNow(),
            ct);
        return result.Succeeded
            ? TypedResults.NoContent()
            : TypedResults.Conflict(new TeamMembershipFailureResponse(
                TeamMembershipMapper.ToProtocol(result.FailureCode!.Value),
                result.ErrorMessage ?? "Team could not be joined."));
    }
}
