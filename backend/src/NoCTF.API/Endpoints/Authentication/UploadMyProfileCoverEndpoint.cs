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

public sealed class UploadMyProfileCoverRequest
{
    public IFormFile File { get; set; } = null!;
}

public sealed class UploadMyProfileCoverValidator : Validator<UploadMyProfileCoverRequest>
{
    public UploadMyProfileCoverValidator() => RuleFor(request => request.File).NotNull();
}

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<ProfileCoverUploadFailureCode>))]
public enum ProfileCoverUploadFailureCode
{
    SizeInvalid,
    SourceMetadataMismatch,
    UnsupportedFormat,
    InvalidDimensions,
    PixelLimitExceeded,
    MultipleFrames,
    MalformedImage
}

public sealed record ProfileCoverUploadFailureResponse(ProfileCoverUploadFailureCode Code);

public sealed class UploadMyProfileCoverEndpoint(
    ReplaceCurrentUserProfileCover replace,
    IUserContext user,
    LinkGenerator links,
    FileUploadLimits uploadLimits,
    TimeProvider timeProvider)
    : Endpoint<UploadMyProfileCoverRequest,
        Results<Ok<CurrentUserResponse>, NotFound,
            BadRequest<ProfileCoverUploadFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Upload)));
        Put("/auth/me/profile-cover");
        AuthSchemes("Bearer");
        AllowFileUploads();
        MaxRequestBodySize(FileUploadLimits.MaximumRequestBytes(
            uploadLimits.MaximumWallpaperBytes));
        Description(builder => builder
            .WithName("Authentication_UploadMyProfileCover")
            .WithMetadata(new EnableRateLimitingAttribute("avatar"))
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status413PayloadTooLarge));
        Summary(summary => summary.Summary = "Replaces the current user's public profile cover image.");
    }

    public override async Task<Results<Ok<CurrentUserResponse>, NotFound,
        BadRequest<ProfileCoverUploadFailureResponse>, ProblemHttpResult>> ExecuteAsync(
        UploadMyProfileCoverRequest request,
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
            return TypedResults.BadRequest(new ProfileCoverUploadFailureResponse(
                ToProtocolFailure(result.Failure.Value)));
        }

        return TypedResults.Ok(CurrentUserMapping.ToResponse(result.Profile!, links, HttpContext));
    }

    private static ProblemHttpResult UploadTooLarge(long maximumBytes) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: "Profile cover is too large.",
            detail: $"Profile cover uploads cannot exceed {maximumBytes} bytes.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = FileUploadFailureCode.UploadTooLarge.ToString()
            });

    private static ProfileCoverUploadFailureCode ToProtocolFailure(WallpaperImageFailure failure) =>
        failure switch
        {
            WallpaperImageFailure.SizeInvalid => ProfileCoverUploadFailureCode.SizeInvalid,
            WallpaperImageFailure.SourceMetadataMismatch =>
                ProfileCoverUploadFailureCode.SourceMetadataMismatch,
            WallpaperImageFailure.UnsupportedFormat => ProfileCoverUploadFailureCode.UnsupportedFormat,
            WallpaperImageFailure.InvalidDimensions => ProfileCoverUploadFailureCode.InvalidDimensions,
            WallpaperImageFailure.PixelLimitExceeded => ProfileCoverUploadFailureCode.PixelLimitExceeded,
            WallpaperImageFailure.MultipleFrames => ProfileCoverUploadFailureCode.MultipleFrames,
            WallpaperImageFailure.MalformedImage => ProfileCoverUploadFailureCode.MalformedImage,
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };
}
