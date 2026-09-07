using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.Platform;
using NoCTF.API.Security;
using NoCTF.Application.Exports;

namespace NoCTF.API.Endpoints.Administration.Archives;

public sealed class ExportPlatformAuditArchiveRequest
{
    public PlatformAuditKindProtocol? Kind { get; set; }
    public Guid? CompetitionId { get; set; }
    public Guid? ActorId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public sealed class ExportPlatformAuditArchiveValidator
    : Validator<ExportPlatformAuditArchiveRequest>
{
    public ExportPlatformAuditArchiveValidator()
    {
        RuleFor(request => request.Kind).IsInEnum().When(request => request.Kind is not null);
        RuleFor(request => request).Must(request =>
                request.From is null && request.To is null
                || request.From is not null && request.To is not null
                && request.From <= request.To)
            .WithMessage("Specify no audit range, or a complete range with the start before the end.");
    }
}

public sealed class ExportPlatformAuditArchiveEndpoint(
    ExportPlatformAuditArchive export,
    IUserContext user)
    : Endpoint<ExportPlatformAuditArchiveRequest,
        Results<FileStreamHttpResult, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Download)));
        Post("/admin/platform/audit-logs/data-export");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminExportPlatformAuditArchive"));
        Summary(summary =>
        {
            summary.Summary = "Streams a bounded platform audit archive.";
            summary.Description =
                "Exports projections of immutable competition events and platform-administration audit facts as NDJSON.";
        });
    }

    public override async Task<
        Results<FileStreamHttpResult, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        ExportPlatformAuditArchiveRequest request,
        CancellationToken ct)
    {
        var result = await export.ExecuteAsync(new ExportPlatformAuditArchiveCommand(
            user.UserId,
            user.IsAdministrator,
            user.IsHuman,
            request.Kind is null ? null : PlatformAuditProtocolMapper.ToDomain(request.Kind.Value),
            request.CompetitionId,
            request.ActorId,
            request.From,
            request.To), ct);
        if (result.Failure == SynchronousArchiveFailure.Forbidden)
            return TypedResults.Forbid();
        if (result.Failure is not null || result.Archive is null)
            return SynchronousArchiveHttpResults.Problem(result.Failure);

        return TypedResults.Stream(
            result.Archive.Content,
            result.Archive.ContentType,
            result.Archive.FileName);
    }
}
