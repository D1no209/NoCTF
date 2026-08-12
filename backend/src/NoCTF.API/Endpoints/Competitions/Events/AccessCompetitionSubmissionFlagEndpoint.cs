using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Gameplay;

namespace NoCTF.API.Endpoints.Competitions.Events;

public sealed record AccessCompetitionGameplayFactValueResponse(
    Guid GameplayFactId,
    GameplayFactKindProtocol GameplayFactKind,
    string Value,
    DateTimeOffset AccessedAt);

public sealed class AccessCompetitionGameplayFactValueEndpoint(
    AccessGameplayFactValue access,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<
        Results<Ok<AccessCompetitionGameplayFactValueResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/flag-access");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminAccessCompetitionGameplayFactValue"));
        Summary(summary =>
        {
            summary.Summary = "Explicitly reads one protected submitted Flag.";
            summary.Description =
                "Administrator, owner, manager, and judge only. Owner, manager, and judge access appends an immutable audit event; platform Administrator access is not audited.";
        });
    }

    public override async Task<
        Results<Ok<AccessCompetitionGameplayFactValueResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
        ExecuteAsync(CancellationToken cancellationToken)
    {
        var result = await access.ExecuteAsync(new GameplayFactValueAccessCommand(
            Route<Guid>("competitionId"),
            Route<Guid>("gameplayFactId"),
            user.UserId,
            timeProvider.GetUtcNow()), cancellationToken);
        if (result.State == CompetitionEventReadState.Forbidden)
            return TypedResults.Forbid();
        if (result.State == CompetitionEventReadState.CompetitionNotFound)
            return TypedResults.NotFound();
        if (result.State != CompetitionEventReadState.Available
            || result.View is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Flag access request.");
        }
        return TypedResults.Ok(new AccessCompetitionGameplayFactValueResponse(
            result.View.GameplayFactId,
            GameplayFactMapper.ToProtocol(result.View.GameplayFactKind),
            result.View.Value,
            result.View.AccessedAt));
    }
}
