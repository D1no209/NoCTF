using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.Application.Administration.PlatformConfiguration;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class UploadPlatformLogoRequest
{
    public IFormFile File { get; set; } = null!;
    public long? ExpectedRevision { get; set; }
}

public sealed class UploadPlatformLogoValidator : Validator<UploadPlatformLogoRequest>
{
    private static readonly string[] SupportedContentTypes =
        ["image/jpeg", "image/png", "image/webp"];

    public UploadPlatformLogoValidator()
    {
        RuleFor(request => request.File).NotNull();
        RuleFor(request => request.File.Length)
            .InclusiveBetween(1, PlatformConfigurationRules.MaximumLogoBytes)
            .When(request => request.File is not null);
        RuleFor(request => request.File.ContentType)
            .Must(contentType => SupportedContentTypes.Contains(
                contentType,
                StringComparer.OrdinalIgnoreCase))
            .When(request => request.File is not null)
            .WithMessage("Logo must be a JPEG, PNG, or WebP image.");
        RuleFor(request => request.ExpectedRevision).NotNull().GreaterThan(0);
    }
}

public sealed class UploadPlatformLogoEndpoint(
    ManagePlatformConfiguration configuration,
    LinkGenerator links)
    : Endpoint<UploadPlatformLogoRequest,
        Results<Ok<PlatformConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/configuration/logo");
        AuthSchemes("Bearer");
        Roles("Administrator");
        AllowFileUploads();
        Description(builder => builder.WithName("AdminPlatformUploadLogo"));
        Summary(summary =>
        {
            summary.Summary = "Replaces the public platform logo.";
            summary.Description = "Stores a validated raster logo and advances the platform configuration revision.";
        });
    }

    public override async Task<
        Results<Ok<PlatformConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        UploadPlatformLogoRequest request,
        CancellationToken ct)
    {
        await using var content = new MemoryStream((int)request.File.Length);
        await request.File.CopyToAsync(content, ct);
        var result = await configuration.ReplaceLogoAsync(
            request.File.FileName,
            request.File.ContentType,
            content.ToArray(),
            request.ExpectedRevision!.Value,
            DateTimeOffset.UtcNow,
            ct);
        return result.State switch
        {
            PlatformLogoUpdateState.Updated => TypedResults.Ok(
                PlatformConfigurationMapping.ToResponse(
                    result.Configuration!,
                    links,
                    HttpContext)),
            PlatformLogoUpdateState.RevisionConflict => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Platform configuration changed.",
                detail: "Reload the configuration before uploading the logo again.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "platform_configuration_conflict"
                }),
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
