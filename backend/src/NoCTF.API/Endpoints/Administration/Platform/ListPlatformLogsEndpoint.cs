using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration.PlatformLogs;
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
    public Guid? CompetitionId { get; set; }
    [QueryParam]
    public Guid? RuntimeInstanceId { get; set; }
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
            .Matches("^[0-9]+-[0-9]+$")
            .When(request => !string.IsNullOrWhiteSpace(request.Cursor));
        RuleFor(request => request).Must(request =>
                request.From is null || request.To is null || request.From <= request.To)
            .WithMessage("From must not be later than To.");
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
    Guid? RuntimeInstanceId);

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
            view.RuntimeInstanceId);
}

public sealed class ListPlatformLogsEndpoint(ObservePlatform platform)
    : Endpoint<
        ListPlatformLogsRequest,
        Results<Ok<PlatformLogListResponse>, ProblemHttpResult>>
{
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
        var result = await platform.QueryLogsAsync(
            new(
                request.MinimumLevel,
                request.Service,
                request.From,
                request.To,
                request.CompetitionId,
                request.RuntimeInstanceId,
                request.Cursor,
                request.Limit),
            ct);
        return result.State == PlatformLogReadState.Available
            ? TypedResults.Ok(new PlatformLogListResponse(
                result.Items.Select(PlatformLogMapping.ToResponse).ToArray(),
                result.NextCursor))
            : TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Platform logs are unavailable.",
                detail: "The bounded Redis log history could not be queried.");
    }
}
