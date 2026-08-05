using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.API.Pagination;
using NoCTF.Infrastructure.Observability;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class ListPlatformLogsRequest
{
    [QueryParam]
    public PlatformLogLevel MinimumLevel { get; set; } = PlatformLogLevel.Warning;
    [QueryParam]
    public PlatformLogService? Service { get; set; }
    [QueryParam]
    public DateTimeOffset? From { get; set; }
    [QueryParam]
    public DateTimeOffset? To { get; set; }
    [QueryParam]
    public string? Category { get; set; }
    [QueryParam]
    public string? Search { get; set; }
    [QueryParam]
    public Guid? CompetitionId { get; set; }
    [QueryParam]
    public Guid? RuntimeInstanceId { get; set; }
    [QueryParam]
    public Guid? TeamId { get; set; }
    [QueryParam]
    public Guid? UserId { get; set; }
    [QueryParam]
    public Guid? CompetitionChallengeId { get; set; }
    [QueryParam]
    public Guid? SubmissionId { get; set; }
    [QueryParam]
    public string? Cursor { get; set; }
    [QueryParam]
    public int Limit { get; set; } = 100;
}

public sealed class ListPlatformLogsValidator : Validator<ListPlatformLogsRequest>
{
    public ListPlatformLogsValidator()
    {
        RuleFor(request => request.MinimumLevel).IsInEnum();
        RuleFor(request => request.Service).IsInEnum().When(request => request.Service is not null);
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
        RuleFor(request => request.Cursor)
            .MaximumLength(2_048)
            .When(request => !string.IsNullOrWhiteSpace(request.Cursor));
        RuleFor(request => request.Category).MaximumLength(512);
        RuleFor(request => request.Search).MaximumLength(256);
        RuleFor(request => request).Must(request =>
                request.From is null || request.To is null
                || request.From <= request.To
                && request.To - request.From <= TimeSpan.FromDays(14))
            .WithMessage("The platform log query range must be between zero and 14 days.");
    }
}

public sealed record PlatformLogResponse(
    string Cursor,
    DateTimeOffset Timestamp,
    PlatformLogService Service,
    PlatformLogLevel Level,
    string Category,
    int EventId,
    string? EventName,
    string Message,
    string? ExceptionType,
    string? ExceptionMessage,
    Guid? CompetitionId,
    Guid? RuntimeInstanceId,
    Guid? TeamId,
    Guid? UserId,
    Guid? CompetitionChallengeId,
    Guid? SubmissionId);

public sealed record PlatformLogListResponse(
    IReadOnlyList<PlatformLogResponse> Items,
    string? NextCursor);

internal static class PlatformLogMapping
{
    public static PlatformLogResponse ToResponse(PlatformLogView view) =>
        new(
            view.Cursor,
            view.Timestamp,
            view.Service,
            view.Level,
            view.Category,
            view.EventId,
            view.EventName,
            PlatformLogRedactor.Redact(view.Message, []),
            view.ExceptionType,
            view.ExceptionMessage is null
                ? null
                : PlatformLogRedactor.Redact(view.ExceptionMessage, []),
            view.CompetitionId,
            view.RuntimeInstanceId,
            view.TeamId,
            view.UserId,
            view.CompetitionChallengeId,
            view.SubmissionId);
}

public sealed class ListPlatformLogsEndpoint(
    ObservePlatform platform,
    SignedKeysetCursor cursors)
    : Endpoint<
        ListPlatformLogsRequest,
        Results<Ok<PlatformLogListResponse>, ProblemHttpResult>>
{
    private const string CursorEndpoint = "admin.platform.logs.list";

    public override void Configure()
    {
        Get("/admin/platform/logs");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Options(options => options
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status503ServiceUnavailable));
        Description(builder => builder.WithName("AdminPlatformListLogs"));
        Summary(summary =>
        {
            summary.Summary = "Queries redacted platform logs.";
            summary.Description =
                "Returns bounded API, Worker, and Runner history. Warning is the default minimum level.";
        });
    }

    public override async Task<
        Results<Ok<PlatformLogListResponse>, ProblemHttpResult>> ExecuteAsync(
        ListPlatformLogsRequest request,
        CancellationToken ct)
    {
        var filterKey = FilterKey(request);
        if (!cursors.TryDecodeOpaque(
                request.Cursor,
                CursorEndpoint,
                filterKey,
                out var position))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid cursor.");
        }
        var result = await platform.QueryLogsAsync(
            new(
                request.MinimumLevel,
                request.Service,
                request.From,
                request.To,
                Normalize(request.Category),
                Normalize(request.Search),
                request.CompetitionId,
                request.RuntimeInstanceId,
                request.TeamId,
                request.UserId,
                request.CompetitionChallengeId,
                request.SubmissionId,
                position,
                request.Limit),
            ct);
        return result.State == PlatformLogReadState.Available
            ? TypedResults.Ok(new PlatformLogListResponse(
                result.Items.Select(PlatformLogMapping.ToResponse).ToArray(),
                result.NextCursor is null
                    ? null
                    : cursors.EncodeOpaque(
                        CursorEndpoint,
                        filterKey,
                        result.NextCursor)))
            : TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Platform logs are unavailable.",
                detail: "The bounded Redis log history could not be queried.");
    }

    internal static string FilterKey(ListPlatformLogsRequest request) =>
        string.Join(
            '|',
            request.MinimumLevel,
            request.Service,
            request.From?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            request.To?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            Normalize(request.Category)?.ToUpperInvariant(),
            Normalize(request.Search)?.ToUpperInvariant(),
            request.CompetitionId,
            request.RuntimeInstanceId,
            request.TeamId,
            request.UserId,
            request.CompetitionChallengeId,
            request.SubmissionId);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
