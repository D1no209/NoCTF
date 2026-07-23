using System.Text.Json;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Notifications;

namespace NoCTF.API.Endpoints.Notifications;

public sealed class ListNotificationsRequest
{
    [QueryParam]
    public string? Cursor { get; set; }
    [QueryParam]
    public int Limit { get; set; } = 50;
}

public sealed class ListNotificationsValidator : Validator<ListNotificationsRequest>
{
    public ListNotificationsValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
}

public sealed record NotificationResponse(
    Guid Id,
    Guid? CompetitionId,
    Guid? EntityId,
    NotificationKind Kind,
    JsonElement Payload,
    DateTimeOffset CreatedAt);

public sealed record NotificationListResponse(
    IReadOnlyList<NotificationResponse> Items,
    string? NextCursor);

public sealed class ListNotificationsEndpoint(
    ListNotifications list,
    SignedKeysetCursor cursors,
    IUserContext user)
    : Endpoint<ListNotificationsRequest,
        Results<Ok<NotificationListResponse>, ProblemHttpResult>>
{
    private const string CursorEndpoint = "notifications.list";

    public override void Configure()
    {
        Get("/notifications");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "List permanent notifications";
            summary.Description = "Returns the current user's immutable notification event stream.";
        });
    }

    public override async Task<
        Results<Ok<NotificationListResponse>, ProblemHttpResult>> ExecuteAsync(
        ListNotificationsRequest request,
        CancellationToken ct)
    {
        if (!cursors.TryDecode(
                request.Cursor,
                CursorEndpoint,
                string.Empty,
                out var position))
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid cursor.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "cursor_invalid"
                });

        var items = await list.ExecuteAsync(
            user.UserId,
            position?.CreatedAt,
            position?.Id,
            request.Limit,
            ct);
        var response = items.Select(item => new NotificationResponse(
            item.Id,
            item.CompetitionId,
            item.EntityId,
            item.Kind,
            JsonSerializer.Deserialize<JsonElement>(item.PayloadJson),
            item.CreatedAt)).ToArray();
        var next = items.Count == request.Limit
            ? cursors.Encode(
                CursorEndpoint,
                string.Empty,
                new(items[^1].CreatedAt, items[^1].Id))
            : null;
        return TypedResults.Ok(new NotificationListResponse(response, next));
    }
}
