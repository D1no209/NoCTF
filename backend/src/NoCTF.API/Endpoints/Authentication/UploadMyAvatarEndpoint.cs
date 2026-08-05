using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class UploadMyAvatarRequest
{
    public IFormFile File { get; set; } = null!;
}

public sealed class UploadMyAvatarValidator : Validator<UploadMyAvatarRequest>
{
    public UploadMyAvatarValidator() => RuleFor(request => request.File).NotNull();
}

[JsonConverter(typeof(JsonStringEnumConverter<AvatarUploadFailureCode>))]
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
    LinkGenerator links)
    : Endpoint<UploadMyAvatarRequest,
        Results<Ok<CurrentUserResponse>, NotFound, BadRequest<AvatarUploadFailureResponse>>>
{
    public override void Configure()
    {
        Post("/auth/me/avatar");
        AuthSchemes("Bearer");
        AllowFileUploads();
        MaxRequestBodySize(UserProfileRules.MaximumAvatarRequestBytes);
        Description(builder => builder
            .WithName("Authentication_UploadMyAvatar")
            .WithMetadata(new EnableRateLimitingAttribute("avatar")));
        Summary(summary => summary.Summary = "Replaces the current user's cropped avatar.");
    }

    public override async Task<Results<Ok<CurrentUserResponse>, NotFound,
        BadRequest<AvatarUploadFailureResponse>>> ExecuteAsync(
        UploadMyAvatarRequest request,
        CancellationToken ct)
    {
        await using var content = new MemoryStream((int)request.File.Length);
        await request.File.CopyToAsync(content, ct);
        var result = await replace.ExecuteAsync(
            user.UserId,
            request.File.FileName,
            request.File.ContentType,
            content.ToArray(),
            DateTimeOffset.UtcNow,
            ct);
        if (result.UserNotFound)
            return TypedResults.NotFound();
        if (result.Failure is not null)
            return TypedResults.BadRequest(new AvatarUploadFailureResponse(
                ToProtocolFailure(result.Failure.Value)));

        return TypedResults.Ok(CurrentUserMapping.ToResponse(result.Profile!, links, HttpContext));
    }

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
