using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class CreateCompetitionWebhookTestDeliveryRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TargetId { get; set; }
}

public sealed record CompetitionWebhookTestAcceptedResponse(
    Guid DeliveryId,
    string StatusUrl,
    CompetitionWebhookTestStateProtocol State);

public sealed class CreateCompetitionWebhookTestDeliveryEndpoint(
    ICompetitionWebhookTestStatusStore statuses,
    IBackendMessagePublisher messages,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<CreateCompetitionWebhookTestDeliveryRequest,
        Results<Accepted<CompetitionWebhookTestAcceptedResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/webhooks/{targetId}/test-deliveries");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCreateCompetitionWebhookTestDelivery"));
    }

    public override async Task<Results<Accepted<CompetitionWebhookTestAcceptedResponse>, ForbidHttpResult>> ExecuteAsync(
        CreateCompetitionWebhookTestDeliveryRequest request,
        CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var now = timeProvider.GetUtcNow();
        var deliveryId = Guid.CreateVersion7(now);
        var message = new TestCompetitionWebhook(
            request.CompetitionId,
            request.TargetId,
            deliveryId,
            now);
        await statuses.CreateAsync(new(
            deliveryId,
            request.CompetitionId,
            request.TargetId,
            CompetitionWebhookTestState.Pending,
            now), ct);
        await messages.TestCompetitionWebhookAsync(message, ct);
        var statusUrl = $"/api/v1/admin/competitions/{request.CompetitionId}/webhooks/{request.TargetId}/test-deliveries/{deliveryId}";
        return TypedResults.Accepted(statusUrl, new CompetitionWebhookTestAcceptedResponse(
            deliveryId,
            statusUrl,
            CompetitionWebhookTestStateProtocol.Pending));
    }
}
