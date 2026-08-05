using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration.PlatformLogs;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class ExportPlatformLogsRequest
{
    [QueryParam] public PlatformLogLevel MinimumLevel { get; set; } = PlatformLogLevel.Warning;
    [QueryParam] public PlatformLogService? Service { get; set; }
    [QueryParam] public DateTimeOffset From { get; set; }
    [QueryParam] public DateTimeOffset To { get; set; }
    [QueryParam] public string? Category { get; set; }
    [QueryParam] public string? Search { get; set; }
    [QueryParam] public Guid? CompetitionId { get; set; }
    [QueryParam] public Guid? RuntimeInstanceId { get; set; }
    [QueryParam] public Guid? TeamId { get; set; }
    [QueryParam] public Guid? UserId { get; set; }
    [QueryParam] public Guid? CompetitionChallengeId { get; set; }
    [QueryParam] public Guid? SubmissionId { get; set; }
}

public sealed class ExportPlatformLogsValidator : Validator<ExportPlatformLogsRequest>
{
    public ExportPlatformLogsValidator()
    {
        RuleFor(request => request.MinimumLevel).IsInEnum();
        RuleFor(request => request.Service).IsInEnum().When(request => request.Service is not null);
        RuleFor(request => request.From).NotEmpty();
        RuleFor(request => request.To).NotEmpty();
        RuleFor(request => request.Category).MaximumLength(512);
        RuleFor(request => request.Search).MaximumLength(256);
        RuleFor(request => request).Must(request =>
                request.From <= request.To
                && request.To - request.From <= TimeSpan.FromDays(14))
            .WithMessage("The platform log export range must be between zero and 14 days.");
    }
}

public sealed class ExportPlatformLogsEndpoint(ExportPlatformLogs export)
    : Endpoint<ExportPlatformLogsRequest,
        Results<FileStreamHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/platform/logs/export");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformExportLogs"));
        Options(builder => builder
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status503ServiceUnavailable));
        Summary(summary =>
        {
            summary.Summary = "Exports bounded platform runtime logs as JSON Lines.";
            summary.Description =
                "Exports at most 50,000 redacted API, Worker, and Runner entries within the 14-day retention window.";
        });
    }

    public override async Task<Results<FileStreamHttpResult, ProblemHttpResult>> ExecuteAsync(
        ExportPlatformLogsRequest request,
        CancellationToken ct)
    {
        var result = await export.ExecuteAsync(new PlatformLogQuery(
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
            null,
            50_000), ct);
        if (result.State != PlatformLogReadState.Available || result.Export is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Platform logs are unavailable.");
        }
        return TypedResults.Stream(
            result.Export.Content,
            "application/x-ndjson",
            result.Export.FileName);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
