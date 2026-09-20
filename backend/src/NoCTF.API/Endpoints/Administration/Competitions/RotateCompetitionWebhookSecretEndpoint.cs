using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class RotateCompetitionWebhookSecretRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TargetId { get; set; }
}

public sealed record RotateCompetitionWebhookSecretResponse(
    string SigningSecret,
    DateTimeOffset PreviousSecretValidUntil);

public sealed class RotateCompetitionWebhookSecretEndpoint(
    RotateCompetitionWebhookSecret rotate,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<RotateCompetitionWebhookSecretRequest,
        Results<Ok<RotateCompetitionWebhookSecretResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/webhooks/{targetId}/rotate-secret");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminRotateCompetitionWebhookSecret"));
    }

    public override async Task<Results<Ok<RotateCompetitionWebhookSecretResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        RotateCompetitionWebhookSecretRequest request,
        CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var result = await rotate.ExecuteAsync(
            request.CompetitionId,
            request.TargetId,
            timeProvider.GetUtcNow(),
            ct);
        if (!result.Succeeded
            || result.SigningSecret is null
            || result.Target?.PreviousSecretValidUntil is not DateTimeOffset validUntil)
            return TypedResults.NotFound();
        return TypedResults.Ok(new RotateCompetitionWebhookSecretResponse(
            result.SigningSecret,
            validUntil));
    }
}
