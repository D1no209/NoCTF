using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;

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
    public int Limit { get; set; } = 100;
}

public sealed class ListPlatformAuditLogsValidator : Validator<ListPlatformAuditLogsRequest>
{
    public ListPlatformAuditLogsValidator()
    {
        RuleFor(request => request.Kind).IsInEnum().When(request => request.Kind is not null);
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
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
    UserAccountLifecycleAction? UserAccountAction,
    string? SubjectDisplayName,
    string? Reason,
    bool Automatic,
    DateTimeOffset OccurredAt);

public sealed record PlatformAuditLogListResponse(
    IReadOnlyList<PlatformAuditLogResponse> Items);

public sealed class ListPlatformAuditLogsEndpoint(ObservePlatform platform)
    : Endpoint<ListPlatformAuditLogsRequest, Ok<PlatformAuditLogListResponse>>
{
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
                "Aggregates existing competition and user-account lifecycle audits without duplicating business history.";
        });
    }

    public override async Task<Ok<PlatformAuditLogListResponse>> ExecuteAsync(
        ListPlatformAuditLogsRequest request,
        CancellationToken ct) =>
        TypedResults.Ok(new PlatformAuditLogListResponse(
            (await platform.QueryAuditsAsync(
                new(
                    request.Kind,
                    request.From,
                    request.To,
                    request.CompetitionId,
                    request.ActorId,
                    request.Limit),
                ct))
            .Select(view => new PlatformAuditLogResponse(
                view.Id,
                view.Kind,
                view.SubjectId,
                view.CompetitionId,
                view.ActorId,
                view.FromCompetitionStatus,
                view.ToCompetitionStatus,
                view.UserAccountAction,
                view.SubjectDisplayName,
                view.Reason,
                view.Automatic,
                view.OccurredAt))
            .ToArray()));
}
