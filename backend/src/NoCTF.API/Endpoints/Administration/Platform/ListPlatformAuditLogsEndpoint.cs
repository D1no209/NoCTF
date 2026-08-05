using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Submissions;
using NoCTF.API.Pagination;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class ListPlatformAuditLogsRequest
{
    [QueryParam]
    public PlatformAuditKind? Kind { get; set; }
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
    PlatformAuditKind Kind,
    Guid SubjectId,
    Guid? CompetitionId,
    Guid? ActorId,
    CompetitionStatus? FromCompetitionStatus,
    CompetitionStatus? ToCompetitionStatus,
    CompetitionLeaderboardVisibility? FromLeaderboardVisibility,
    CompetitionLeaderboardVisibility? ToLeaderboardVisibility,
    UserAccountLifecycleAction? UserAccountAction,
    CompetitionEventKind? CompetitionEventKind,
    CompetitionEventLevel? CompetitionEventLevel,
    CompetitionEventVisibility? CompetitionEventVisibility,
    Guid? RelatedUserId,
    Guid? TeamId,
    Guid? CompetitionChallengeId,
    Guid? RuntimeInstanceId,
    Guid? SubmissionId,
    Guid? ScoringEventId,
    Guid? QuestionId,
    SubmissionKind? SubmissionKind,
    SubmissionEvaluationState? SubmissionState,
    ScoringEventKind? ScoringEventKind,
    ScoringResult? ScoringResult,
    string? SubjectDisplayName,
    string? Reason,
    bool Automatic,
    DateTimeOffset OccurredAt);

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
                    request.Kind,
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
                view.Kind,
                view.SubjectId,
                view.CompetitionId,
                view.ActorId,
                view.FromCompetitionStatus,
                view.ToCompetitionStatus,
                view.FromLeaderboardVisibility,
                view.ToLeaderboardVisibility,
                view.UserAccountAction,
                view.CompetitionEventKind,
                view.CompetitionEventLevel,
                view.CompetitionEventVisibility,
                view.RelatedUserId,
                view.TeamId,
                view.CompetitionChallengeId,
                view.RuntimeInstanceId,
                view.SubmissionId,
                view.ScoringEventId,
                view.QuestionId,
                view.SubmissionKind,
                view.SubmissionState,
                view.ScoringEventKind,
                view.ScoringResult,
                view.SubjectDisplayName,
                view.Reason,
                view.Automatic,
                view.OccurredAt))
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
