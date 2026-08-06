using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Profiles;

namespace NoCTF.API.Endpoints.Teams;

public sealed class JoinTeamByInvitationRequest
{
    public string InvitationToken { get; set; } = string.Empty;
}

public sealed class JoinTeamByInvitationRequestValidator
    : Validator<JoinTeamByInvitationRequest>
{
    public JoinTeamByInvitationRequestValidator()
    {
        RuleFor(request => request.InvitationToken).Length(32);
    }
}

public sealed class JoinTeamByInvitationEndpoint(JoinTeamProfile join, IUserContext user)
    : Endpoint<JoinTeamByInvitationRequest, Results<Ok<GlobalTeamResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/teams/join");
        AuthSchemes("Bearer");
    }

    public override async Task<Results<Ok<GlobalTeamResponse>, ProblemHttpResult>> ExecuteAsync(
        JoinTeamByInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await join.ExecuteAsync(
            request.InvitationToken,
            user.UserId,
            cancellationToken);
        return result.Succeeded
            ? TypedResults.Ok(GlobalTeamMapper.ToResponse(result.Value!, user.UserId))
            : TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Team was not joined.",
                detail: result.ErrorMessage);
    }
}
