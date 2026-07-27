using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed record StartValidationResponse(IReadOnlyList<StartGateError> Errors);

public sealed class ValidateCompetitionStartEndpoint(
    CompetitionStartGate gate,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<Ok<StartValidationResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/start-validation");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminValidateCompetitionStart"));
        Summary(summary =>
        {
            summary.Summary = "Validates the competition start gate.";
            summary.Description = "Returns every current stable, deduplicated start error without changing state.";
        });
    }

    public override async Task<
        Results<Ok<StartValidationResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var errors = await gate.ValidateAsync(competitionId, ct);
        return errors is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new StartValidationResponse(errors));
    }
}
