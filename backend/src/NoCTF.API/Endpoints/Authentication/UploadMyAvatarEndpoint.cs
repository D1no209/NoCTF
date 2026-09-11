using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Storage;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class UploadMyAvatarRequest
{
    public IFormFile File { get; set; } = null!;
}

public sealed class UploadMyAvatarValidator : Validator<UploadMyAvatarRequest>
{
    public UploadMyAvatarValidator() => RuleFor(request => request.File).NotNull();
}

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<AvatarUploadFailureCode>))]
public enum AvatarUploadFailureCode
{
    SizeInvalid,
    SourceMetadataMismatch,
    UnsupportedFormat,
    InvalidDimensions,
    PixelLimitExceeded,
    MultipleFrames,
    MalformedImage
}

public sealed record AvatarUploadFailureResponse(AvatarUploadFailureCode Code);

public sealed class UploadMyAvatarEndpoint(
    ReplaceCurrentUserAvatar replace,
    IUserContext user,
    LinkGenerator links,
    FileUploadLimits uploadLimits,
    TimeProvider timeProvider)
    : Endpoint<UploadMyAvatarRequest,
        Results<Ok<CurrentUserResponse>, NotFound, BadRequest<AvatarUploadFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Upload)));
        Put("/auth/me/avatar");
        AuthSchemes("Bearer");
        AllowFileUploads();
        MaxRequestBodySize(FileUploadLimits.MaximumRequestBytes(
            uploadLimits.MaximumAvatarBytes));
        Description(builder => builder
            .WithName("Authentication_UploadMyAvatar")
            .WithMetadata(new EnableRateLimitingAttribute("avatar"))
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status413PayloadTooLarge));
        Summary(summary => summary.Summary = "Replaces the current user's cropped avatar.");
    }

    public override async Task<Results<Ok<CurrentUserResponse>, NotFound,
        BadRequest<AvatarUploadFailureResponse>, ProblemHttpResult>> ExecuteAsync(
        UploadMyAvatarRequest request,
        CancellationToken ct)
    {
        if (request.File.Length > uploadLimits.MaximumAvatarBytes)
            return UploadTooLarge(uploadLimits.MaximumAvatarBytes);
        await using var content = new MemoryStream((int)request.File.Length);
        await request.File.CopyToAsync(content, ct);
        var result = await replace.ExecuteAsync(
            user.UserId,
            request.File.FileName,
            request.File.ContentType,
            content.ToArray(),
            uploadLimits.MaximumAvatarBytes,
            timeProvider.GetUtcNow(),
            ct);
        if (result.UserNotFound)
            return TypedResults.NotFound();
        if (result.Failure is not null)
            return TypedResults.BadRequest(new AvatarUploadFailureResponse(
                ToProtocolFailure(result.Failure.Value)));

        return TypedResults.Ok(CurrentUserMapping.ToResponse(result.Profile!, links, HttpContext));
    }

    private static ProblemHttpResult UploadTooLarge(long maximumBytes) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: "Avatar is too large.",
            detail: $"Avatar uploads cannot exceed {maximumBytes} bytes.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = FileUploadFailureCode.UploadTooLarge.ToString()
            });

    private static AvatarUploadFailureCode ToProtocolFailure(AvatarImageFailure failure) =>
        failure switch
        {
            AvatarImageFailure.SizeInvalid => AvatarUploadFailureCode.SizeInvalid,
            AvatarImageFailure.SourceMetadataMismatch =>
                AvatarUploadFailureCode.SourceMetadataMismatch,
            AvatarImageFailure.UnsupportedFormat => AvatarUploadFailureCode.UnsupportedFormat,
            AvatarImageFailure.InvalidDimensions => AvatarUploadFailureCode.InvalidDimensions,
            AvatarImageFailure.PixelLimitExceeded => AvatarUploadFailureCode.PixelLimitExceeded,
            AvatarImageFailure.MultipleFrames => AvatarUploadFailureCode.MultipleFrames,
            AvatarImageFailure.MalformedImage => AvatarUploadFailureCode.MalformedImage,
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };
}
