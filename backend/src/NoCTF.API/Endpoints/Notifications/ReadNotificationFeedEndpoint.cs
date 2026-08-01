using System.Text.Json;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Notifications;

namespace NoCTF.API.Endpoints.Notifications;

public sealed class ReadNotificationFeedRequest
{
    [QueryParam]
    public string? Cursor { get; set; }

    [QueryParam]
    public int Limit { get; set; } = 100;
}

public sealed class ReadNotificationFeedValidator : Validator<ReadNotificationFeedRequest>
{
    public ReadNotificationFeedValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
}

public sealed record NotificationFeedResponse(
    IReadOnlyList<NotificationResponse> Items,
    string NextCursor);

public sealed class ReadNotificationFeedEndpoint(
    ReadNotificationFeed feed,
    SignedKeysetCursor cursors,
    IUserContext user)
    : Endpoint<ReadNotificationFeedRequest,
        Results<Ok<NotificationFeedResponse>, ProblemHttpResult>>
{
    private const string CursorEndpoint = "notifications.feed";

    public override void Configure()
    {
        Get("/notifications/feed");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("ReadNotificationFeed"));
        Summary(summary =>
        {
            summary.Summary = "Read the current identity's incremental notification feed.";
            summary.Description =
                "The first request returns an empty safe checkpoint. Later requests return events in ascending creation order.";
        });
    }

    public override async Task<
        Results<Ok<NotificationFeedResponse>, ProblemHttpResult>> ExecuteAsync(
        ReadNotificationFeedRequest request,
        CancellationToken ct)
    {
        var cursorScope = user.UserId.ToString("N");
        if (!cursors.TryDecode(
                request.Cursor,
                CursorEndpoint,
                cursorScope,
                out var decoded))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid cursor.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "cursor_invalid"
                });
        }

        if (decoded is null)
        {
            var checkpoint = await feed.GetCheckpointAsync(
                user.UserId,
                DateTimeOffset.UtcNow,
                ct);
            return TypedResults.Ok(new NotificationFeedResponse(
                [],
                Encode(cursorScope, checkpoint)));
        }

        var position = new KeysetNotificationPosition(decoded.CreatedAt, decoded.Id);
        var items = await feed.ExecuteAsync(
            user.UserId,
            position,
            request.Limit,
            ct);
        var next = items.Count == 0
            ? position
            : new KeysetNotificationPosition(items[^1].CreatedAt, items[^1].Id);
        return TypedResults.Ok(new NotificationFeedResponse(
            items.Select(item => new NotificationResponse(
                item.Id,
                item.CompetitionId,
                item.EntityId,
                item.Kind,
                JsonSerializer.Deserialize<JsonElement>(item.PayloadJson),
                item.CreatedAt)).ToArray(),
            Encode(cursorScope, next)));
    }

    private string Encode(string cursorScope, KeysetNotificationPosition position) =>
        cursors.Encode(
            CursorEndpoint,
            cursorScope,
            new(position.CreatedAt, position.Id));
}
