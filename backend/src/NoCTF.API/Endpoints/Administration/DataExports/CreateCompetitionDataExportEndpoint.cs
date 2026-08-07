using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.DataExports;
using NoCTF.Domain.DataExports;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.DataExports;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<DataExportScopeProtocol>))]
public enum DataExportScopeProtocol
{
    CompetitionArchive,
    PlatformAudit
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<DataExportStatusProtocol>))]
public enum DataExportStatusProtocol
{
    Queued,
    Processing,
    Available,
    Failed,
    Expired
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<DataExportFailureCodeProtocol>))]
public enum DataExportFailureCodeProtocol
{
    SubjectNotFound,
    SizeLimitExceeded,
    GenerationFailed,
    ObjectStorageFailed
}

[Mapper]
internal static partial class DataExportProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial DataExportScopeProtocol ToProtocol(DataExportScope value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial DataExportStatusProtocol ToProtocol(DataExportStatus value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial DataExportFailureCodeProtocol ToProtocol(DataExportFailureCode value);
}

public sealed class CreateCompetitionDataExportRequest
{
    public bool IncludeProtectedFlags { get; set; }
    public string? Reason { get; set; }
}

public sealed class CreateCompetitionDataExportValidator
    : Validator<CreateCompetitionDataExportRequest>
{
    public CreateCompetitionDataExportValidator()
    {
        RuleFor(request => request.Reason).MaximumLength(512);
    }
}

public sealed record DataExportResponse(
    Guid Id,
    DataExportScopeProtocol Scope,
    Guid? CompetitionId,
    Guid RequestedByUserId,
    DateTimeOffset RequestedAt,
    bool IncludeProtectedFlags,
    string? Reason,
    DataExportStatusProtocol Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    string? FileName,
    long? Length,
    string? Sha256,
    DataExportFailureCodeProtocol? FailureCode,
    string? FailureDetail);

public sealed record DataExportListResponse(IReadOnlyList<DataExportResponse> Items);

public sealed class CreateCompetitionDataExportEndpoint(
    RequestDataExport requestExport,
    IUserContext user)
    : Endpoint<CreateCompetitionDataExportRequest,
        Results<Accepted<DataExportResponse>, Conflict<DataExportResponse>, NotFound,
            ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/data-exports");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCreateCompetitionDataExport"));
        Summary(summary =>
        {
            summary.Summary = "Queues a complete competition archive.";
            summary.Description =
                "Administrator, competition owner, and manager only. Plaintext Flags are excluded unless a human Administrator explicitly supplies a reason.";
        });
    }

    public override async Task<
        Results<Accepted<DataExportResponse>, Conflict<DataExportResponse>, NotFound,
            ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        CreateCompetitionDataExportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await requestExport.ExecuteAsync(new RequestDataExportCommand(
            DataExportScope.CompetitionArchive,
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            user.IsHuman,
            request.IncludeProtectedFlags,
            request.Reason), cancellationToken);
        if (result.Failure == RequestDataExportFailure.ActiveExportExists
            && result.Export is not null)
        {
            return TypedResults.Conflict(Map(result.Export));
        }
        if (result.Failure == RequestDataExportFailure.SubjectNotFound)
            return TypedResults.NotFound();
        if (result.Failure is RequestDataExportFailure.Forbidden
            or RequestDataExportFailure.ProtectedFlagsRequireAdministrator
            or RequestDataExportFailure.ProtectedFlagsRequireHuman)
        {
            return TypedResults.Forbid();
        }
        if (result.Failure is not null || result.Export is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The data export request is invalid.",
                detail: result.Failure?.ToString());
        }
        return TypedResults.Accepted<DataExportResponse>(
            (string?)null,
            Map(result.Export));
    }

    internal static DataExportResponse Map(DataExportView item) => new(
        item.Id,
        DataExportProtocolMapper.ToProtocol(item.Scope),
        item.CompetitionId,
        item.RequestedByUserId,
        item.RequestedAt,
        item.IncludeProtectedFlags,
        item.Reason,
        DataExportProtocolMapper.ToProtocol(item.Status),
        item.StartedAt,
        item.CompletedAt,
        item.ExpiresAt,
        item.FileName,
        item.Length,
        item.Sha256,
        item.FailureCode is null ? null : DataExportProtocolMapper.ToProtocol(item.FailureCode.Value),
        item.FailureDetail);
}
