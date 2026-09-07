using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Endpoints.Competitions.Events;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Endpoints.Teams;
using NoCTF.API.Serialization;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.API.Pagination;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Platform;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<PlatformAuditKindProtocol>))]
public enum PlatformAuditKindProtocol
{
    CompetitionLifecycle,
    UserAccountLifecycle,
    PlatformAdministration,
    CompetitionAdministration,
    CompetitionLeaderboardVisibility,
    CompetitionEvent
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<PlatformAdministrationActionProtocol>))]
public enum PlatformAdministrationActionProtocol
{
    AuditArchiveExported
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<UserAccountLifecycleActionProtocol>))]
public enum UserAccountLifecycleActionProtocol
{
    Activated,
    Banned,
    Disabled,
    EmailVerified,
    EmailUnverified,
    Anonymized,
    PhysicallyDeleted
}

[Mapper]
internal static partial class PlatformAuditProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)] public static partial PlatformAuditKindProtocol ToProtocol(PlatformAuditKind value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial PlatformAuditKind ToDomain(PlatformAuditKindProtocol value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial UserAccountLifecycleActionProtocol ToProtocol(UserAccountLifecycleAction value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial PlatformAdministrationActionProtocol ToProtocol(PlatformAdministrationAction value);
}

public sealed class ListPlatformAuditLogsRequest
{
    [QueryParam]
    public PlatformAuditKindProtocol? Kind { get; set; }
    [QueryParam]
    public DateTimeOffset? From { get; set; }
    [QueryParam]
    public DateTimeOffset? To { get; set; }
    [QueryParam]
    public Guid? CompetitionId { get; set; }
    [QueryParam]
    public Guid? ActorId { get; set; }
    [QueryParam]
    public string? Cursor { get; set; }
    [QueryParam]
    public int Limit { get; set; } = 100;
}

public sealed class ListPlatformAuditLogsValidator : Validator<ListPlatformAuditLogsRequest>
{
    public ListPlatformAuditLogsValidator()
    {
        RuleFor(request => request.Kind).IsInEnum().When(request => request.Kind is not null);
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
        RuleFor(request => request.Cursor)
            .MaximumLength(2_048)
            .When(request => !string.IsNullOrWhiteSpace(request.Cursor));
        RuleFor(request => request).Must(request =>
                request.From is null || request.To is null || request.From <= request.To)
            .WithMessage("From must not be later than To.");
    }
}

public sealed record PlatformAuditLogResponse(
    Guid Id,
    PlatformAuditKindProtocol Kind,
    Guid SubjectId,
    Guid? CompetitionId,
    Guid? ActorId,
    CompetitionStatusProtocol? FromCompetitionStatus,
    CompetitionStatusProtocol? ToCompetitionStatus,
    LeaderboardVisibilityProtocol? FromLeaderboardVisibility,
    LeaderboardVisibilityProtocol? ToLeaderboardVisibility,
    UserAccountLifecycleActionProtocol? UserAccountAction,
    PlatformAdministrationActionProtocol? PlatformAdministrationAction,
    CompetitionEventKindProtocol? CompetitionEventKind,
    CompetitionEventLevelProtocol? CompetitionEventLevel,
    CompetitionEventVisibilityProtocol? CompetitionEventVisibility,
    Guid? RelatedUserId,
    Guid? TeamId,
    Guid? CompetitionChallengeId,
    Guid? RuntimeInstanceId,
    Guid? GameplayFactId,
    Guid? QuestionId,
    GameplayFactKindProtocol? GameplayFactKind,
    GameplayFactStateProtocol? GameplayFactState,
    GameplayFactResultProtocol? GameplayFactResult,
    string? SubjectDisplayName,
    string? Reason,
    bool Automatic,
    DateTimeOffset OccurredAt,
    Guid? FileId = null);

public sealed record PlatformAuditLogListResponse(
    IReadOnlyList<PlatformAuditLogResponse> Items,
    string? NextCursor);

public sealed class ListPlatformAuditLogsEndpoint(
    ObservePlatform platform,
    SignedKeysetCursor cursors)
    : Endpoint<ListPlatformAuditLogsRequest,
        Results<Ok<PlatformAuditLogListResponse>, ProblemHttpResult>>
{
    private const string CursorEndpoint = "admin.platform.audit-logs.list";

    public override void Configure()
    {
        Get("/admin/platform/audit-logs");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformListAuditLogs"));
        Summary(summary =>
        {
            summary.Summary = "Queries immutable platform audit facts.";
            summary.Description =
                "Projects immutable competition events and user-account lifecycle audits without duplicating business history.";
        });
    }

    public override async Task<Results<Ok<PlatformAuditLogListResponse>, ProblemHttpResult>>
        ExecuteAsync(
        ListPlatformAuditLogsRequest request,
        CancellationToken ct)
    {
        var filterKey = FilterKey(request);
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, filterKey, out var position))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid cursor.");
        }
        var items = await platform.QueryAuditsAsync(
                new(
                    request.Kind is null ? null : PlatformAuditProtocolMapper.ToDomain(request.Kind.Value),
                    request.From,
                    request.To,
                    request.CompetitionId,
                    request.ActorId,
                    position?.CreatedAt,
                    position?.Id,
                    request.Limit),
                ct);
        var responses = items
            .Select(view => new PlatformAuditLogResponse(
                view.Id,
                PlatformAuditProtocolMapper.ToProtocol(view.Kind),
                view.SubjectId,
                view.CompetitionId,
                view.ActorId,
                view.FromCompetitionStatus is null ? null : CompetitionProtocolMapper.ToProtocol(view.FromCompetitionStatus.Value),
                view.ToCompetitionStatus is null ? null : CompetitionProtocolMapper.ToProtocol(view.ToCompetitionStatus.Value),
                view.FromLeaderboardVisibility is null ? null : CompetitionProtocolMapper.ToProtocol(view.FromLeaderboardVisibility.Value),
                view.ToLeaderboardVisibility is null ? null : CompetitionProtocolMapper.ToProtocol(view.ToLeaderboardVisibility.Value),
                view.UserAccountAction is null ? null : PlatformAuditProtocolMapper.ToProtocol(view.UserAccountAction.Value),
                view.PlatformAdministrationAction is null ? null : PlatformAuditProtocolMapper.ToProtocol(view.PlatformAdministrationAction.Value),
                view.CompetitionEventKind is null ? null : CompetitionEventProtocolMapper.ToProtocol(view.CompetitionEventKind.Value),
                view.CompetitionEventLevel is null ? null : CompetitionEventProtocolMapper.ToProtocol(view.CompetitionEventLevel.Value),
                view.CompetitionEventVisibility is null ? null : CompetitionEventProtocolMapper.ToProtocol(view.CompetitionEventVisibility.Value),
                view.RelatedUserId,
                view.TeamId,
                view.CompetitionChallengeId,
                view.RuntimeInstanceId,
                view.GameplayFactId,
                view.QuestionId,
                view.GameplayFactKind is null ? null : GameplayFactMapper.ToProtocol(view.GameplayFactKind.Value),
                view.GameplayFactState is null ? null : GameplayFactMapper.ToProtocol(view.GameplayFactState.Value),
                view.GameplayFactResult is null ? null : GameplayFactMapper.ToProtocol(view.GameplayFactResult.Value),
                view.SubjectDisplayName,
                view.Reason,
                view.Automatic,
                view.OccurredAt, view.FileId))
            .ToArray();
        return TypedResults.Ok(new PlatformAuditLogListResponse(
            responses,
            items.Count == request.Limit
                ? cursors.Encode(
                    CursorEndpoint,
                    filterKey,
                    new(items[^1].OccurredAt, items[^1].Id))
                : null));
    }

    internal static string FilterKey(ListPlatformAuditLogsRequest request) =>
        string.Join(
            '|',
            request.Kind,
            request.From?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            request.To?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            request.CompetitionId,
            request.ActorId);
}
