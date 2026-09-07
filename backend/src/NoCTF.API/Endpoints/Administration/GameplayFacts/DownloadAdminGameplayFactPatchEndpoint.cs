using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.Observability;
using NoCTF.Hosting.Observability;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<AdminPatchFailureCode>))]
public enum AdminPatchFailureCode
{
    Forbidden, SubmissionNotFound, NotFixSubmission, PatchNotFound,
    InvalidAssociation, FileNotFound, StorageUnavailable, AuditUnavailable
}

public sealed record AdminPatchMetadataResponse(Guid FileId, string FileName, long ByteLength, DateTimeOffset UploadedAt, string Sha256);

// OpenAPI binary-body marker; ExecuteAsync returns a streamed FileStreamHttpResult, not JSON.
[NJsonSchema.Annotations.JsonSchema(NJsonSchema.JsonObjectType.String, Format = "binary")]
public sealed class AdminPatchBinaryResponse;

public sealed class DownloadAdminGameplayFactPatchRequest
{
    public Guid CompetitionId { get; set; }
    public Guid GameplayFactId { get; set; }
}

public sealed class DownloadAdminGameplayFactPatchValidator : Validator<DownloadAdminGameplayFactPatchRequest>
{
    public DownloadAdminGameplayFactPatchValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();
        RuleFor(x => x.GameplayFactId).NotEmpty();
    }
}

[Mapper]
internal static partial class AdminPatchMapping
{
    public static partial AdminPatchMetadataResponse ToResponse(AdminPatchMetadata value);
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial AdminPatchFailureCode ToProtocol(AdminPatchFailure failure);
}

public sealed class DownloadAdminGameplayFactPatchEndpoint(AccessAdminPatch patches, IUserContext user, TimeProvider clock)
    : Endpoint<DownloadAdminGameplayFactPatchRequest, Results<FileStreamHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/patch");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new ApiRequestMetricsMetadata(ApiRequestKind.Download)));
        Description(builder => builder.WithName("AdminDownloadGameplayFactPatch")
            .Produces<AdminPatchBinaryResponse>(StatusCodes.Status200OK, "application/octet-stream")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable));
        Summary(summary =>
        {
            summary.Summary = "Downloads the original Patch archive with a mandatory access audit.";
            summary.Description = "Only platform administrators and this competition's owner, managers and judges may download. Observers and participants are forbidden. Evaluation results do not restrict access.";
        });
    }

    public override async Task<Results<FileStreamHttpResult, ProblemHttpResult>> ExecuteAsync(DownloadAdminGameplayFactPatchRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        HttpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
        var factId = Route<Guid>("gameplayFactId");
        var result = await patches.DownloadAsync(Route<Guid>("competitionId"), factId, user.UserId, clock.GetUtcNow(), ct);
        if (result.Failure is { } failure)
        {
            var (status, detail) = failure switch
            {
                AdminPatchFailure.Forbidden => (StatusCodes.Status403Forbidden, "Only this competition's owner, managers, judges, or a platform administrator may download Patch archives."),
                AdminPatchFailure.SubmissionNotFound => (StatusCodes.Status404NotFound, "The submission does not exist in this competition."),
                AdminPatchFailure.NotFixSubmission => (StatusCodes.Status400BadRequest, "Only Fix submissions have a downloadable Patch archive."),
                AdminPatchFailure.PatchNotFound => (StatusCodes.Status404NotFound, "This submission has no Patch archive."),
                AdminPatchFailure.InvalidAssociation => (StatusCodes.Status409Conflict, "The Patch archive association does not match the submission's competition, challenge, team or actor."),
                AdminPatchFailure.FileNotFound => (StatusCodes.Status404NotFound, "The Patch archive file no longer exists."),
                AdminPatchFailure.StorageUnavailable => (StatusCodes.Status503ServiceUnavailable, "Patch archive storage is temporarily unavailable. Try again later."),
                AdminPatchFailure.AuditUnavailable => (StatusCodes.Status503ServiceUnavailable, "The Patch download audit could not be saved. No file was returned. Try again later."),
                _ => throw new InvalidOperationException($"Unknown Patch access failure: {failure}.")
            };
            return TypedResults.Problem(statusCode: status, title: "Patch download failed.", detail: detail,
                extensions: new Dictionary<string, object?> { ["code"] = AdminPatchMapping.ToProtocol(failure) });
        }
        return TypedResults.Stream(result.Content!, "application/octet-stream", SafeFileName(result.Metadata!.FileName, factId), enableRangeProcessing: false);
    }

    public static string SafeFileName(string fileName, Guid factId)
    {
        var leaf = Path.GetFileName(fileName.Replace('\\', '/'));
        var safe = string.Concat(leaf.Where(c => !char.IsControl(c))).Trim();
        return string.IsNullOrWhiteSpace(safe) || safe is "." or ".." ? $"patch-{factId:N}.tar.gz" : safe;
    }
}
