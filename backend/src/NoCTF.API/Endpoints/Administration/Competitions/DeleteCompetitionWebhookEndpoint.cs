using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class DeleteCompetitionWebhookRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TargetId { get; set; }
}

public sealed class DeleteCompetitionWebhookEndpoint(
    DeleteCompetitionWebhookTarget delete,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<DeleteCompetitionWebhookRequest,
        Results<NoContent, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/webhooks/{targetId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDeleteCompetitionWebhook"));
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult>> ExecuteAsync(
        DeleteCompetitionWebhookRequest request,
        CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var failure = await delete.ExecuteAsync(request.CompetitionId, request.TargetId, ct);
        return failure is null ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
