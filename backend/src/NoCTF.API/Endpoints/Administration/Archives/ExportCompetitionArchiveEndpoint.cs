using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Exports;

namespace NoCTF.API.Endpoints.Administration.Archives;

public sealed class ExportCompetitionArchiveRequest
{
    public bool IncludeProtectedFlags { get; set; }
    public string? Reason { get; set; }
}

public sealed class ExportCompetitionArchiveValidator
    : Validator<ExportCompetitionArchiveRequest>
{
    public ExportCompetitionArchiveValidator()
    {
        RuleFor(request => request.Reason).MaximumLength(512);
    }
}

public sealed class ExportCompetitionArchiveEndpoint(
    ExportCompetitionArchive export,
    IUserContext user)
    : Endpoint<ExportCompetitionArchiveRequest,
        Results<FileStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Download)));
        Post("/admin/competitions/{competitionId}/data-export");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminExportCompetitionArchive"));
        Summary(summary =>
        {
            summary.Summary = "Streams a bounded competition archive.";
            summary.Description =
                "Administrator, competition owner, and manager only. Protected Flag values require an administrator and an explicit reason.";
        });
    }

    public override async Task<
        Results<FileStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        ExportCompetitionArchiveRequest request,
        CancellationToken ct)
    {
        var result = await export.ExecuteAsync(new ExportCompetitionArchiveCommand(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            request.IncludeProtectedFlags,
            request.Reason), ct);
        if (result.Failure == SynchronousArchiveFailure.SubjectNotFound)
            return TypedResults.NotFound();
        if (result.Failure is SynchronousArchiveFailure.Forbidden
            or SynchronousArchiveFailure.ProtectedFlagsRequireAdministrator)
        {
            return TypedResults.Forbid();
        }
        if (result.Failure is not null || result.Archive is null)
            return SynchronousArchiveHttpResults.Problem(result.Failure);

        return TypedResults.Stream(
            result.Archive.Content,
            result.Archive.ContentType,
            result.Archive.FileName);
    }
}

internal static class SynchronousArchiveHttpResults
{
    internal static ProblemHttpResult Problem(SynchronousArchiveFailure? failure)
    {
        var code = failure ?? SynchronousArchiveFailure.GenerationFailed;
        var status = code switch
        {
            SynchronousArchiveFailure.ReasonRequired
                or SynchronousArchiveFailure.InvalidQuery => StatusCodes.Status400BadRequest,
            SynchronousArchiveFailure.RecordLimitExceeded
                or SynchronousArchiveFailure.CompressedSizeLimitExceeded
                or SynchronousArchiveFailure.MemoryLimitExceeded => StatusCodes.Status413PayloadTooLarge,
            SynchronousArchiveFailure.TimeLimitExceeded => StatusCodes.Status408RequestTimeout,
            _ => StatusCodes.Status500InternalServerError
        };
        return ApiProblems.Problem(
            statusCode: status,
            title: ApiMessages.Get(ApiMessageId.ExportCompetitionArchiveTitleSynchronousArchiveCouldGenerated),
            extensions: new Dictionary<string, object?> { ["code"] = code.ToString() });
    }
}
