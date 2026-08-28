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
            .WithMessage("Logo must be a JPEG, PNG, or WebP image.");
    }
}

public sealed class UploadPlatformLogoEndpoint(
    ManagePlatformConfiguration configuration,
    LinkGenerator links,
    FileUploadLimits uploadLimits,
    TimeProvider timeProvider)
    : Endpoint<UploadPlatformLogoRequest,
        Results<Ok<PlatformConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/configuration/logo");
        AuthSchemes("Bearer");
        Roles("Administrator");
        AllowFileUploads();
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
        Results<Ok<PlatformConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        UploadPlatformLogoRequest request,
        CancellationToken ct)
    {
        if (request.File.Length > uploadLimits.MaximumLogoBytes)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Logo is too large.",
                detail: $"Logo uploads cannot exceed {uploadLimits.MaximumLogoBytes} bytes.",
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
            PlatformLogoUpdateState.InvalidSize => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Logo size is invalid."),
            PlatformLogoUpdateState.InvalidFormat => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Logo format is invalid."),
            _ => throw new InvalidOperationException(
                $"Unsupported platform logo update state: {result.State}.")
        };
    }
}
