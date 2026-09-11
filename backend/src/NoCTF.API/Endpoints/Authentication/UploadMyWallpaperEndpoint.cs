using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class UploadMyWallpaperRequest
{
    public IFormFile File { get; set; } = null!;
}

public sealed class UploadMyWallpaperValidator : Validator<UploadMyWallpaperRequest>
{
    public UploadMyWallpaperValidator() => RuleFor(request => request.File).NotNull();
}

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<WallpaperUploadFailureCode>))]
public enum WallpaperUploadFailureCode
{
    SizeInvalid,
    SourceMetadataMismatch,
    UnsupportedFormat,
    InvalidDimensions,
    PixelLimitExceeded,
    MultipleFrames,
    MalformedImage
}

public sealed record WallpaperUploadFailureResponse(WallpaperUploadFailureCode Code);

public sealed class UploadMyWallpaperEndpoint(
    ReplaceCurrentUserWallpaper replace,
    IUserContext user,
    LinkGenerator links,
    FileUploadLimits uploadLimits,
    TimeProvider timeProvider)
    : Endpoint<UploadMyWallpaperRequest,
        Results<Ok<CurrentUserResponse>, NotFound,
            BadRequest<WallpaperUploadFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Upload)));
        Put("/auth/me/wallpaper");
        AuthSchemes("Bearer");
        AllowFileUploads();
        MaxRequestBodySize(FileUploadLimits.MaximumRequestBytes(
            uploadLimits.MaximumWallpaperBytes));
        Description(builder => builder
            .WithName("Authentication_UploadMyWallpaper")
            .WithMetadata(new EnableRateLimitingAttribute("avatar"))
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status413PayloadTooLarge));
        Summary(summary => summary.Summary = "Replaces and enables the current user's wallpaper.");
    }

    public override async Task<Results<Ok<CurrentUserResponse>, NotFound,
        BadRequest<WallpaperUploadFailureResponse>, ProblemHttpResult>> ExecuteAsync(
        UploadMyWallpaperRequest request,
        CancellationToken ct)
    {
        if (request.File.Length > uploadLimits.MaximumWallpaperBytes)
            return UploadTooLarge(uploadLimits.MaximumWallpaperBytes);

        await using var content = new MemoryStream((int)request.File.Length);
        await request.File.CopyToAsync(content, ct);
        var result = await replace.ExecuteAsync(
            user.UserId,
            request.File.FileName,
            request.File.ContentType,
            content.ToArray(),
            uploadLimits.MaximumWallpaperBytes,
            timeProvider.GetUtcNow(),
            ct);
        if (result.UserNotFound)
            return TypedResults.NotFound();
        if (result.Failure is not null)
        {
            return TypedResults.BadRequest(new WallpaperUploadFailureResponse(
                ToProtocolFailure(result.Failure.Value)));
        }

        return TypedResults.Ok(CurrentUserMapping.ToResponse(result.Profile!, links, HttpContext));
    }

    private static ProblemHttpResult UploadTooLarge(long maximumBytes) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: "Wallpaper is too large.",
            detail: $"Wallpaper uploads cannot exceed {maximumBytes} bytes.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = FileUploadFailureCode.UploadTooLarge.ToString()
            });

    private static WallpaperUploadFailureCode ToProtocolFailure(WallpaperImageFailure failure) =>
        failure switch
        {
            WallpaperImageFailure.SizeInvalid => WallpaperUploadFailureCode.SizeInvalid,
            WallpaperImageFailure.SourceMetadataMismatch =>
                WallpaperUploadFailureCode.SourceMetadataMismatch,
            WallpaperImageFailure.UnsupportedFormat => WallpaperUploadFailureCode.UnsupportedFormat,
            WallpaperImageFailure.InvalidDimensions => WallpaperUploadFailureCode.InvalidDimensions,
            WallpaperImageFailure.PixelLimitExceeded => WallpaperUploadFailureCode.PixelLimitExceeded,
            WallpaperImageFailure.MultipleFrames => WallpaperUploadFailureCode.MultipleFrames,
            WallpaperImageFailure.MalformedImage => WallpaperUploadFailureCode.MalformedImage,
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };
}
