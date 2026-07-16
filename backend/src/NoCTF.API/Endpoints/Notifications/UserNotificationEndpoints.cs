using System.Security.Claims;
using FastEndpoints;
using NoCTF.Application.Notifications;

namespace NoCTF.API.Endpoints.Notifications;

public sealed class GetUserNotificationsRequest
{
    public int Limit { get; set; } = 20;
}

public sealed class MarkUserNotificationReadRequest
{
    public Guid Id { get; set; }
}

public sealed class GetUserNotificationsEndpoint(IUserNotificationService notifications)
    : Endpoint<GetUserNotificationsRequest, UserNotificationPage>
{
    public override void Configure()
    {
        Get("/api/notifications");
        Claims(ClaimTypes.NameIdentifier);
        Options(builder => builder.RequireRateLimiting("public-read"));
    }

    public override async Task HandleAsync(GetUserNotificationsRequest req, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        await SendAsync(await notifications.GetAsync(userId, req.Limit, ct), cancellation: ct);
    }

    private bool TryGetUserId(out Guid userId)
        => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out userId);
}

public sealed class MarkUserNotificationReadEndpoint(IUserNotificationService notifications)
    : Endpoint<MarkUserNotificationReadRequest>
{
    public override void Configure()
    {
        Post("/api/notifications/{id}/read");
        Claims(ClaimTypes.NameIdentifier);
        Options(builder => builder.RequireRateLimiting("competition-submit"));
    }

    public override async Task HandleAsync(MarkUserNotificationReadRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        if (!await notifications.MarkReadAsync(userId, req.Id, ct))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendNoContentAsync(ct);
    }
}

public sealed class MarkAllUserNotificationsReadEndpoint(IUserNotificationService notifications)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/notifications/read-all");
        Claims(ClaimTypes.NameIdentifier);
        Options(builder => builder.RequireRateLimiting("competition-submit"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        await notifications.MarkAllReadAsync(userId, ct);
        await SendNoContentAsync(ct);
    }
}
