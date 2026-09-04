using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Registration;

namespace NoCTF.API.Endpoints.Teams;

public sealed class ResubmitTeamRegistrationEndpoint(
    ResubmitTeamRegistration resubmit,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound,
        Conflict<TeamRegistrationFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/teams/{teamId}/registration/resubmit");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Resubmits a rejected team registration.");
    }

    public override async Task<Results<NoContent, NotFound,
        Conflict<TeamRegistrationFailureResponse>>> ExecuteAsync(CancellationToken ct)
    {
        var result = await resubmit.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("teamId"),
            user.UserId,
            ct);
        if (result.FailureCode == TeamRegistrationFailure.CompetitionNotFound)
            return TypedResults.NotFound();
        return result.Succeeded
            ? TypedResults.NoContent()
            : TypedResults.Conflict(new TeamRegistrationFailureResponse(
                TeamMapper.ToProtocol(result.FailureCode!.Value),
                result.ErrorMessage ?? "The rejected registration could not be resubmitted."));
    }
}
