using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class UploadPlatformLogoRequest
{
    public IFormFile File { get; set; } = null!;
}

public sealed class UploadPlatformLogoValidator : Validator<UploadPlatformLogoRequest>
{
    private static readonly string[] SupportedContentTypes =
        ["image/jpeg", "image/png", "image/webp"];

    public UploadPlatformLogoValidator()
    {
        RuleFor(request => request.File).NotNull();
        RuleFor(request => request.File.Length)
            .GreaterThan(0)
            .When(request => request.File is not null);
        RuleFor(request => request.File.ContentType)
            .Must(contentType => SupportedContentTypes.Contains(
                contentType,
                StringComparer.OrdinalIgnoreCase))
            .When(request => request.File is not null)
            .WithMessage(_ => ApiMessages.Text(ApiMessageId.UploadPlatformLogoValidationLogoJpegPngWebp)).WithErrorCode(ApiMessages.Key(ApiMessageId.UploadPlatformLogoValidationLogoJpegPngWebp));
    }
}

public sealed class UploadPlatformLogoEndpoint(
    ManagePlatformConfiguration configuration,
    LinkGenerator links,
    FileUploadLimits uploadLimits,
    TimeProvider timeProvider)
    : Endpoint<UploadPlatformLogoRequest,
        Results<Ok<PlatformBrandingResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Upload)));
        Put("/admin/platform/configuration/logo");
        AuthSchemes("Bearer");
        Roles("Administrator");
        AllowFileUploads();
        Description(builder => builder.Accepts<UploadPlatformLogoRequest>("multipart/form-data"));
        MaxRequestBodySize(FileUploadLimits.MaximumRequestBytes(
            uploadLimits.MaximumLogoBytes));
        Description(builder => builder
            .WithName("AdminPlatformUploadLogo")
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status413PayloadTooLarge));
        Summary(summary =>
        {
            summary.Summary = "Replaces the public platform logo.";
            summary.Description = "Stores and applies a validated raster logo.";
        });
    }

    public override async Task<
        Results<Ok<PlatformBrandingResponse>, ProblemHttpResult>> ExecuteAsync(
        UploadPlatformLogoRequest request,
        CancellationToken ct)
    {
        if (request.File.Length > uploadLimits.MaximumLogoBytes)
            return ApiProblems.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: ApiMessages.Get(ApiMessageId.UploadPlatformLogoTitleLogoTooLarge),
                detail: ApiMessages.Get(ApiMessageId.UploadSizeLimit, new Dictionary<string, object?> { ["maximumBytes"] = uploadLimits.MaximumLogoBytes }),
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = FileUploadFailureCode.UploadTooLarge.ToString()
                });
        await using var content = new MemoryStream((int)request.File.Length);
        await request.File.CopyToAsync(content, ct);
        var result = await configuration.ReplaceLogoAsync(
            request.File.FileName,
            request.File.ContentType,
            content.ToArray(),
            uploadLimits.MaximumLogoBytes,
            timeProvider.GetUtcNow(),
            ct);
        return result.State switch
        {
            PlatformLogoUpdateState.Updated => TypedResults.Ok(
                PlatformConfigurationMapping.ToResponse(
                    result.Configuration!,
                    links,
                    HttpContext)),
            PlatformLogoUpdateState.InvalidSize => ApiProblems.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ApiMessages.Get(ApiMessageId.UploadPlatformLogoTitleLogoSizeInvalid)),
            PlatformLogoUpdateState.InvalidFormat => ApiProblems.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ApiMessages.Get(ApiMessageId.UploadPlatformLogoTitleLogoFormatInvalid)),
            _ => throw new InvalidOperationException(
                $"Unsupported platform logo update state: {result.State}.")
        };
    }
}
