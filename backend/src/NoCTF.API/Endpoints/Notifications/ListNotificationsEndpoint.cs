using System.Text.Json;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Notifications;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Notifications;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<NotificationKindProtocol>))]
public enum NotificationKindProtocol
{
    CompetitionLifecycleChanged,
    TeamRegistrationChanged,
    SubmissionEvaluated,
    RuntimeStateChanged,
    StartGateFailed,
    ManagementFailure,
    BloodAwarded,
    ChallengePublished,
    HintPublished,
    TeamBanned,
    CompetitionQuestionOpened,
    CompetitionQuestionReplied,
    CompetitionQuestionStatusChanged,
    CheatIncidentDetected,
    TeamBanCorrected,
    DataExportReady,
    DataExportFailed
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<NotificationFailureCode>))]
public enum NotificationFailureCode
{
    CursorInvalid
}

[Mapper]
internal static partial class NotificationProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial NotificationKindProtocol ToProtocol(NotificationKind value);
}

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
    NotificationKindProtocol Kind,
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
                user.UserId.ToString("N"),
                out var position))
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid cursor.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = NotificationFailureCode.CursorInvalid
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
            NotificationProtocolMapper.ToProtocol(item.Kind),
            JsonSerializer.Deserialize<JsonElement>(item.PayloadJson),
            item.CreatedAt)).ToArray();
        var next = items.Count == request.Limit
            ? cursors.Encode(
                CursorEndpoint,
                user.UserId.ToString("N"),
                new(items[^1].CreatedAt, items[^1].Id))
            : null;
        return TypedResults.Ok(new NotificationListResponse(response, next));
    }
}
