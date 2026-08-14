using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed record AssignTeamTrackRequest(
    string TrackKey,
    long ExpectedTeamVersion);

public sealed record AssignTeamTrackResponse(
    Guid CompetitionId,
    Guid TeamId,
    string TrackKey,
    long TeamVersion);

public sealed class AssignTeamTrackValidator : Validator<AssignTeamTrackRequest>
{
    public AssignTeamTrackValidator()
    {
        RuleFor(request => request.TrackKey).NotEmpty().MaximumLength(64);
        RuleFor(request => request.ExpectedTeamVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class AssignTeamTrackEndpoint(
    AssignTeamTrack assign,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<AssignTeamTrackRequest,
        Results<
            Ok<AssignTeamTrackResponse>,
            Conflict<CompetitionTrackFailureResponse>,
            NotFound,
            ForbidHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/teams/{teamId}/track");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminTeamTrack_Assign"));
        Summary(summary =>
        {
            summary.Summary = "Assigns a team to a competition track.";
            summary.Description = "Allows competition administrators to move a team to an existing public or internal track, including after start.";
        });
    }

    public override async Task<Results<
        Ok<AssignTeamTrackResponse>,
        Conflict<CompetitionTrackFailureResponse>,
        NotFound,
        ForbidHttpResult>> ExecuteAsync(
        AssignTeamTrackRequest request,
        CancellationToken cancellationToken)
    {
        var competitionId = Route<Guid>("competitionId");
        var teamId = Route<Guid>("teamId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, cancellationToken))
            return TypedResults.Forbid();
        var result = await assign.ExecuteAsync(new AssignTeamTrackCommand(
            competitionId,
            teamId,
            request.TrackKey,
            request.ExpectedTeamVersion,
            user.UserId,
            DateTimeOffset.UtcNow), cancellationToken);
        if (result.FailureCode is CompetitionTrackFailureCode.CompetitionNotFound
            or CompetitionTrackFailureCode.TeamNotFound)
            return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.Conflict(CompetitionTrackFailureMapping.ToResponse(
                result.FailureCode!.Value,
                result.ErrorMessage));
        return TypedResults.Ok(new AssignTeamTrackResponse(
            result.Value!.CompetitionId,
            result.Value.TeamId,
            result.Value.TrackKey,
            result.Value.TeamVersion));
    }
}
