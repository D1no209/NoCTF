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

public sealed class ListNotificationsRequest : PaginationRequest
{
    [QueryParam]
    public Guid? CompetitionId { get; set; }
    [QueryParam]
    public NotificationListScopeProtocol Scope { get; set; } = NotificationListScopeProtocol.All;
}

public sealed class ListNotificationsValidator : Validator<ListNotificationsRequest>
{
    public ListNotificationsValidator() =>
        PaginationRules.Add(this);
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

public sealed class NotificationListResponse : ArrayResult<NotificationResponse>
{
    public NotificationListResponse() { }

    public NotificationListResponse(NotificationResponse[] items, int total)
        : base(items, total) { }
}

public sealed class ListNotificationsEndpoint(
    ListNotifications list,
    IUserContext user)
    : Endpoint<ListNotificationsRequest, Ok<NotificationListResponse>>
{
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

    public override async Task<Ok<NotificationListResponse>> ExecuteAsync(
        ListNotificationsRequest request,
        CancellationToken ct)
    {
        var page = await list.ExecutePageAsync(
            user.UserId,
            request.CompetitionId,
            request.Offset,
            request.Limit,
            request.Desc,
            NotificationProtocolMapper.ToDomain(request.Scope),
            ct);
        var response = page.Items.Select(item => new NotificationResponse(
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
        return TypedResults.Ok(new NotificationListResponse(response, page.Total));
    }
}
