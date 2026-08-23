using System.Text.Json;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Notifications;

namespace NoCTF.API.Endpoints.Notifications;

public sealed class ReadNotificationThreadEndpoint(
    INotificationReader reader,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<NotificationListResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/notifications/{notificationId}/thread");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Read an authorized notification thread.";
            summary.Description = "Returns the immutable notification thread in stable chronological order.";
        });
    }

    public override async Task<Results<Ok<NotificationListResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var items = await reader.ReadThreadAsync(
            user.UserId,
            Route<Guid>("notificationId"),
            ct);
        if (items is null)
            return TypedResults.NotFound();
        var response = items.Select(item => new NotificationResponse(
            item.Id,
            item.SourceType,
            item.SourceId,
            item.TargetType,
            item.TargetId,
            NotificationProtocolMapper.ToProtocol(item.Kind),
            JsonSerializer.Deserialize<JsonElement>(item.ContentJson),
            item.RelatedType,
            item.RelatedId,
            item.ThreadRootId,
            item.ReplyToId,
            item.SentAt,
            item.SourceDisplayName)).ToArray();
        return TypedResults.Ok(new NotificationListResponse(response, null));
    }
}
