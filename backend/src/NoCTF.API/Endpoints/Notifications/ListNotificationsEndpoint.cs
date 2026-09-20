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
using NoCTF.Domain.Shared;

namespace NoCTF.API.Endpoints.Notifications;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<NotificationKindProtocol>))]
public enum NotificationKindProtocol
{
    Message,
    CompetitionAnnouncement,
    QuestionOpened,
    QuestionStatusChanged,
    CompetitionLifecycleChanged,
    TeamRegistrationChanged,
    GameplayFactAdjudicated,
    RuntimeStateChanged,
    StartGateFailed,
    ManagementFailure,
    BloodAwarded,
    ChallengePublished,
    HintPublished,
    TeamBanned,
    CheatIncidentDetected,
    TeamBanCorrected,
    TeamBanAppealSubmitted,
    PlatformAuditExported,
    UserAccountLifecycleChanged,
    CompetitionForceDeleted
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<NotificationFailureCode>))]
public enum NotificationFailureCode
{
    CursorInvalid
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<NotificationListScopeProtocol>))]
public enum NotificationListScopeProtocol
{
    All,
    Inbox
}

[Mapper]
internal static partial class NotificationProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    [MapperIgnoreSourceValue(NotificationKind.AuthenticationSecurityActivity)]
    [MapperIgnoreSourceValue(NotificationKind.HttpCommandReceipt)]
    [MapperIgnoreSourceValue(NotificationKind.PlatformUserAccessTokenIssued)]
    [MapperIgnoreSourceValue(NotificationKind.PlatformUserAccessTokenRevoked)]
    [MapperIgnoreSourceValue(NotificationKind.PlatformUserTokensInvalidated)]
    [MapperIgnoreSourceValue(NotificationKind.SsoProviderConfigurationChanged)]
    [MapperIgnoreSourceValue(NotificationKind.SsoExternalIdentityBindingChanged)]
    public static partial NotificationKindProtocol ToProtocol(NotificationKind value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial NotificationReadScope ToDomain(NotificationListScopeProtocol value);
}

public sealed class ListNotificationsRequest
{
    [QueryParam]
    public Guid? CompetitionId { get; set; }
    [QueryParam]
    public NotificationListScopeProtocol Scope { get; set; } = NotificationListScopeProtocol.All;
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
    NotificationSourceType SourceType,
    Guid? SourceId,
    NotificationTargetType TargetType,
    Guid TargetId,
    NotificationKindProtocol Kind,
    JsonElement Content,
    EntityReferenceKind? RelatedType,
    Guid? RelatedId,
    Guid? ThreadRootId,
    Guid? ReplyToId,
    DateTimeOffset SentAt,
    string? SourceDisplayName);

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
                CursorScope(user.UserId, request.CompetitionId, request.Scope),
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
            request.CompetitionId,
            position?.CreatedAt,
            position?.Id,
            request.Limit,
            NotificationProtocolMapper.ToDomain(request.Scope),
            ct);
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
        var next = items.Count == request.Limit
            ? cursors.Encode(
                CursorEndpoint,
                CursorScope(user.UserId, request.CompetitionId, request.Scope),
                new(items[^1].SentAt, items[^1].Id))
            : null;
        return TypedResults.Ok(new NotificationListResponse(response, next));
    }

    private static string CursorScope(
        Guid userId,
        Guid? competitionId,
        NotificationListScopeProtocol scope) =>
        string.Join(
            '|',
            userId.ToString("N"),
            competitionId?.ToString("N") ?? "all",
            scope);
}
