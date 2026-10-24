using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class RetryCompetitionWebhookDeliveryRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TargetId { get; set; }
    public Guid EventId { get; set; }
}

public sealed class RetryCompetitionWebhookDeliveryValidator : Validator<RetryCompetitionWebhookDeliveryRequest>
{
    public RetryCompetitionWebhookDeliveryValidator()
    {
        RuleFor(item => item.CompetitionId).NotEmpty();
        RuleFor(item => item.TargetId).NotEmpty();
        RuleFor(item => item.EventId).NotEmpty();
    }
}

public sealed record RetryCompetitionWebhookDeliveryResponse(Guid EventId, Guid TargetId);
public sealed record RetryCompetitionWebhookDeliveryConflict(CompetitionWebhookRetryFailure Failure);

public sealed class RetryCompetitionWebhookDeliveryEndpoint(
    RetryCompetitionWebhookDelivery retry, ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : Endpoint<RetryCompetitionWebhookDeliveryRequest,
        Results<Accepted<RetryCompetitionWebhookDeliveryResponse>, NotFound,
            Conflict<RetryCompetitionWebhookDeliveryConflict>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/webhooks/{targetId}/deliveries/{eventId}/retry");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminRetryCompetitionWebhookDelivery"));
    }

    public override async Task<Results<Accepted<RetryCompetitionWebhookDeliveryResponse>, NotFound,
        Conflict<RetryCompetitionWebhookDeliveryConflict>, ForbidHttpResult>> ExecuteAsync(
        RetryCompetitionWebhookDeliveryRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var failure = await retry.ExecuteAsync(request.CompetitionId, request.TargetId, request.EventId, ct);
        if (failure == CompetitionWebhookRetryFailure.NotFound) return TypedResults.NotFound();
        if (failure is not null) return TypedResults.Conflict(new RetryCompetitionWebhookDeliveryConflict(failure.Value));
        return TypedResults.Accepted(
            $"/api/v1/admin/competitions/{request.CompetitionId}/webhook-deliveries?limit=100",
            new RetryCompetitionWebhookDeliveryResponse(request.EventId, request.TargetId));
    }
}
