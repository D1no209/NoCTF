using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Registration;

namespace NoCTF.API.Endpoints.Teams;

public sealed class RegisterTeamForCompetitionRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
}

public sealed class RegisterTeamForCompetitionRequestValidator
    : Validator<RegisterTeamForCompetitionRequest>
{
    public RegisterTeamForCompetitionRequestValidator()
    {
        RuleFor(request => request.TeamId).NotEmpty();
    }
}

public sealed class RegisterTeamForCompetitionEndpoint(
    RegisterTeamProfile register,
    IUserContext user)
    : Endpoint<RegisterTeamForCompetitionRequest,
        Results<Created<TeamResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/team-registrations");
        AuthSchemes("Bearer");
    }

    public override async Task<
        Results<Created<TeamResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        RegisterTeamForCompetitionRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var result = await register.ExecuteAsync(
            request.CompetitionId,
            request.TeamId,
            user.UserId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (result.ErrorCode is "competition_not_found" or "team_not_found")
            return TypedResults.NotFound();
        if (result.ErrorCode == "team_forbidden")
            return TypedResults.Forbid();
        if (!result.Succeeded)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Team registration was not created.",
                detail: result.ErrorMessage);
        }
        var response = TeamMapper.ToResponse(result.Value!);
        return TypedResults.Created(
            $"/competitions/{request.CompetitionId}/teams/{response.Id}",
            response);
    }
}
