using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
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
    private static readonly string[] SupportedContentTypes =
        ["image/jpeg", "image/png", "image/webp"];

    public UploadMyAvatarValidator()
    {
        RuleFor(request => request.File).NotNull();
        RuleFor(request => request.File.Length)
            .InclusiveBetween(1, UserProfileRules.MaximumAvatarBytes)
            .When(request => request.File is not null);
        RuleFor(request => request.File.ContentType)
            .Must(contentType => SupportedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            .When(request => request.File is not null)
            .WithMessage("Avatar must be a JPEG, PNG, or WebP image.");
    }
}

public sealed class UploadMyAvatarEndpoint(
    ReplaceCurrentUserAvatar replace,
    IUserContext user,
    LinkGenerator links)
    : Endpoint<UploadMyAvatarRequest,
        Results<Ok<CurrentUserResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/me/avatar");
        AuthSchemes("Bearer");
        AllowFileUploads();
        Description(builder => builder.WithName("Authentication_UploadMyAvatar"));
        Summary(summary => summary.Summary = "Replaces the current user's cropped avatar.");
    }

    public override async Task<Results<Ok<CurrentUserResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
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
        if (result.ErrorCode == "user_not_found")
            return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Avatar was not updated.",
                detail: result.ErrorMessage,
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode });

        return TypedResults.Ok(CurrentUserMapping.ToResponse(result.Value!, links, HttpContext));
    }
}
