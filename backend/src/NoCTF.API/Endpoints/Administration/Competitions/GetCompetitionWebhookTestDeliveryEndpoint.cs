using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionWebhookTestStateProtocol>))]
public enum CompetitionWebhookTestStateProtocol
{
    Pending,
    Succeeded,
    Failed
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionWebhookTestFailureCodeProtocol>))]
public enum CompetitionWebhookTestFailureCodeProtocol
{
    TargetUnavailable,
    ReceiverGone,
    PermanentFailure
}

public sealed class GetCompetitionWebhookTestDeliveryRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TargetId { get; set; }
    public Guid DeliveryId { get; set; }
}

public sealed record CompetitionWebhookTestStatusResponse(
    Guid DeliveryId,
    CompetitionWebhookTestStateProtocol State,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    CompetitionWebhookTestFailureCodeProtocol? FailureCode);

public sealed class GetCompetitionWebhookTestDeliveryEndpoint(
    ICompetitionWebhookTestStatusStore statuses,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<GetCompetitionWebhookTestDeliveryRequest,
        Results<Ok<CompetitionWebhookTestStatusResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Summary(summary =>
        {
            summary.Summary = "Returns the current result of a competition webhook test delivery.";
            summary.Description = summary.Summary;
        });

        Get("/admin/competitions/{competitionId}/webhooks/{targetId}/test-deliveries/{deliveryId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCompetitionWebhookTestDelivery"));
    }

    public override async Task<Results<Ok<CompetitionWebhookTestStatusResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        GetCompetitionWebhookTestDeliveryRequest request,
        CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var status = await statuses.GetAsync(request.DeliveryId, ct);
        if (status is null
            || status.CompetitionId != request.CompetitionId
            || status.TargetId != request.TargetId)
            return TypedResults.NotFound();
        return TypedResults.Ok(new CompetitionWebhookTestStatusResponse(
            status.DeliveryId,
            status.State switch
            {
                CompetitionWebhookTestState.Pending => CompetitionWebhookTestStateProtocol.Pending,
                CompetitionWebhookTestState.Succeeded => CompetitionWebhookTestStateProtocol.Succeeded,
                CompetitionWebhookTestState.Failed => CompetitionWebhookTestStateProtocol.Failed,
                _ => throw new ArgumentOutOfRangeException()
            },
            status.RequestedAt,
            status.CompletedAt,
            status.FailureCode is null
                ? null
                : status.FailureCode.Value switch
                {
                    CompetitionWebhookTestFailureCode.TargetUnavailable =>
                        CompetitionWebhookTestFailureCodeProtocol.TargetUnavailable,
                    CompetitionWebhookTestFailureCode.ReceiverGone =>
                        CompetitionWebhookTestFailureCodeProtocol.ReceiverGone,
                    CompetitionWebhookTestFailureCode.PermanentFailure =>
                        CompetitionWebhookTestFailureCodeProtocol.PermanentFailure,
                    _ => throw new ArgumentOutOfRangeException()
                }));
    }
}
