using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Domain.Identity;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class CurrentUserBasicProfilePatchRequest
{
    public required string? Description { get; set; }
}

public sealed class CurrentUserSchoolIdentityPatchRequest
{
    public required string? FullName { get; set; }
    public required string? StudentNumber { get; set; }
}

public sealed class CurrentUserAppearancePatchRequest
{
    public required bool WallpaperEnabled { get; set; }
}

public sealed class PatchMyProfileRequest
{
    public CurrentUserBasicProfilePatchRequest? Profile { get; set; }
    public CurrentUserSchoolIdentityPatchRequest? SchoolIdentity { get; set; }
    public CurrentUserAppearancePatchRequest? Appearance { get; set; }
}

[Flags]
internal enum CurrentUserProfilePatchSection
{
    None = 0,
    Profile = 1 << 0,
    SchoolIdentity = 1 << 1,
    Appearance = 1 << 2
}

public sealed class PatchMyProfileValidator : Validator<PatchMyProfileRequest>
{
    public PatchMyProfileValidator()
    {
        RuleFor(request => request)
            .Must(request => request.Profile is not null
                || request.SchoolIdentity is not null
                || request.Appearance is not null)
            .WithMessage(_ => ApiMessages.Text(ApiMessageId.PatchMyProfileValidationLeastOneProfileSection)).WithErrorCode(ApiMessages.Key(ApiMessageId.PatchMyProfileValidationLeastOneProfileSection));
        RuleFor(request => request.Profile!.Description)
            .MaximumLength(UserProfileRules.MaximumDescriptionLength)
            .When(request => request.Profile is not null);
        RuleFor(request => request.SchoolIdentity!.FullName)
            .MaximumLength(100)
            .Must(value => value?.Any(char.IsControl) != true)
            .When(request => request.SchoolIdentity is not null);
        RuleFor(request => request.SchoolIdentity!.StudentNumber)
            .MaximumLength(64)
            .Must(value => value?.Any(char.IsControl) != true)
            .When(request => request.SchoolIdentity is not null);
    }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CurrentUserProfilePatchFailureCode>))]
public enum CurrentUserProfilePatchFailureCode
{
    WallpaperNotUploaded
}

public sealed record CurrentUserProfilePatchFailureResponse(
    CurrentUserProfilePatchFailureCode Code)
{
    public string Detail => ApiMessages.For(Code).Text;
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

[Mapper(
    AutoUserMappings = false,
    RequiredMappingStrategy = RequiredMappingStrategy.Both)]
public static partial class CurrentUserProfilePatchMapper
{
    [MapperIgnoreTarget(nameof(User.Id))]
    [MapperIgnoreTarget(nameof(User.ConcurrencyStamp))]
    [MapperIgnoreTarget(nameof(User.UserName))]
    [MapperIgnoreTarget(nameof(User.NormalizedUserName))]
    [MapperIgnoreTarget(nameof(User.Email))]
    [MapperIgnoreTarget(nameof(User.PasswordHash))]
    [MapperIgnoreTarget(nameof(User.Kind))]
    [MapperIgnoreTarget(nameof(User.Role))]
    [MapperIgnoreTarget(nameof(User.AccountStatus))]
    [MapperIgnoreTarget(nameof(User.TokenVersion))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityProviderId))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentity))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityProtocol))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityNamespace))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentitySubject))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityBoundAt))]
    [MapperIgnoreTarget(nameof(User.SchoolFullName))]
    [MapperIgnoreTarget(nameof(User.SchoolStudentNumber))]
    [MapperIgnoreTarget(nameof(User.AvatarFileId))]
    [MapperIgnoreTarget(nameof(User.AvatarFile))]
    [MapperIgnoreTarget(nameof(User.ProfileCoverFileId))]
    [MapperIgnoreTarget(nameof(User.ProfileCoverFile))]
    [MapperIgnoreTarget(nameof(User.WallpaperFileId))]
    [MapperIgnoreTarget(nameof(User.WallpaperFile))]
    [MapperIgnoreTarget(nameof(User.WallpaperEnabled))]
    [MapperIgnoreTarget(nameof(User.EmailVerifiedAt))]
    [MapperIgnoreTarget(nameof(User.CreatedAt))]
    [MapperIgnoreTarget(nameof(User.UpdatedAt))]
    [MapperIgnoreTarget(nameof(User.NormalizedEmail))]
    public static partial void ApplyProfileAsSelf(
        CurrentUserBasicProfilePatchRequest request,
        [MappingTarget] User target);

    [MapProperty(
        nameof(CurrentUserSchoolIdentityPatchRequest.FullName),
        nameof(User.SchoolFullName))]
    [MapProperty(
        nameof(CurrentUserSchoolIdentityPatchRequest.StudentNumber),
        nameof(User.SchoolStudentNumber))]
    [MapperIgnoreTarget(nameof(User.Id))]
    [MapperIgnoreTarget(nameof(User.ConcurrencyStamp))]
    [MapperIgnoreTarget(nameof(User.UserName))]
    [MapperIgnoreTarget(nameof(User.NormalizedUserName))]
    [MapperIgnoreTarget(nameof(User.Email))]
    [MapperIgnoreTarget(nameof(User.PasswordHash))]
    [MapperIgnoreTarget(nameof(User.Kind))]
    [MapperIgnoreTarget(nameof(User.Role))]
    [MapperIgnoreTarget(nameof(User.AccountStatus))]
    [MapperIgnoreTarget(nameof(User.TokenVersion))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityProviderId))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentity))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityProtocol))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityNamespace))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentitySubject))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityBoundAt))]
    [MapperIgnoreTarget(nameof(User.Description))]
    [MapperIgnoreTarget(nameof(User.AvatarFileId))]
    [MapperIgnoreTarget(nameof(User.AvatarFile))]
    [MapperIgnoreTarget(nameof(User.ProfileCoverFileId))]
    [MapperIgnoreTarget(nameof(User.ProfileCoverFile))]
    [MapperIgnoreTarget(nameof(User.WallpaperFileId))]
    [MapperIgnoreTarget(nameof(User.WallpaperFile))]
    [MapperIgnoreTarget(nameof(User.WallpaperEnabled))]
    [MapperIgnoreTarget(nameof(User.EmailVerifiedAt))]
    [MapperIgnoreTarget(nameof(User.CreatedAt))]
    [MapperIgnoreTarget(nameof(User.UpdatedAt))]
    [MapperIgnoreTarget(nameof(User.NormalizedEmail))]
    public static partial void ApplySchoolIdentityAsSelf(
        CurrentUserSchoolIdentityPatchRequest request,
        [MappingTarget] User target);

    [MapProperty(
        nameof(CurrentUserAppearancePatchRequest.WallpaperEnabled),
        nameof(User.WallpaperEnabled))]
    [MapperIgnoreTarget(nameof(User.Id))]
    [MapperIgnoreTarget(nameof(User.ConcurrencyStamp))]
    [MapperIgnoreTarget(nameof(User.UserName))]
    [MapperIgnoreTarget(nameof(User.NormalizedUserName))]
    [MapperIgnoreTarget(nameof(User.Email))]
    [MapperIgnoreTarget(nameof(User.PasswordHash))]
    [MapperIgnoreTarget(nameof(User.Kind))]
    [MapperIgnoreTarget(nameof(User.Role))]
    [MapperIgnoreTarget(nameof(User.AccountStatus))]
    [MapperIgnoreTarget(nameof(User.TokenVersion))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityProviderId))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentity))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityProtocol))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityNamespace))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentitySubject))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityBoundAt))]
    [MapperIgnoreTarget(nameof(User.Description))]
    [MapperIgnoreTarget(nameof(User.SchoolFullName))]
    [MapperIgnoreTarget(nameof(User.SchoolStudentNumber))]
    [MapperIgnoreTarget(nameof(User.AvatarFileId))]
    [MapperIgnoreTarget(nameof(User.AvatarFile))]
    [MapperIgnoreTarget(nameof(User.ProfileCoverFileId))]
    [MapperIgnoreTarget(nameof(User.ProfileCoverFile))]
    [MapperIgnoreTarget(nameof(User.WallpaperFileId))]
    [MapperIgnoreTarget(nameof(User.WallpaperFile))]
    [MapperIgnoreTarget(nameof(User.EmailVerifiedAt))]
    [MapperIgnoreTarget(nameof(User.CreatedAt))]
    [MapperIgnoreTarget(nameof(User.UpdatedAt))]
    [MapperIgnoreTarget(nameof(User.NormalizedEmail))]
    public static partial void ApplyAppearanceAsSelf(
        CurrentUserAppearancePatchRequest request,
        [MappingTarget] User target);
}

public sealed class PatchMyProfileEndpoint(
    PatchCurrentUserProfile patch,
    IUserContext user,
    IOptions<AccountPrivacyOptions> privacy)
    : Endpoint<PatchMyProfileRequest,
        Results<Ok<CurrentUserProfileResponse>, NotFound,
            BadRequest<CurrentUserProfilePatchFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Patch("/auth/me/profile");
        AuthSchemes("Bearer");
        Description(builder => builder
            .WithName("Authentication_PatchMyProfile")
            .ProducesProblemFE(StatusCodes.Status400BadRequest));
        Summary(summary => { summary.Summary = "Updates selected current-user profile sections."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<Ok<CurrentUserProfileResponse>, NotFound,
        BadRequest<CurrentUserProfilePatchFailureResponse>, ProblemHttpResult>> ExecuteAsync(
        PatchMyProfileRequest request,
        CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var sections = ResolveSections(request);
        var result = await patch.ExecuteAsync(
            user.UserId,
            target =>
            {
                if ((sections & CurrentUserProfilePatchSection.Profile) != 0)
                    CurrentUserProfilePatchMapper.ApplyProfileAsSelf(request.Profile!, target);
                if ((sections & CurrentUserProfilePatchSection.SchoolIdentity) != 0)
                    CurrentUserProfilePatchMapper.ApplySchoolIdentityAsSelf(
                        request.SchoolIdentity!,
                        target);
                if ((sections & CurrentUserProfilePatchSection.Appearance) != 0)
                    CurrentUserProfilePatchMapper.ApplyAppearanceAsSelf(request.Appearance!, target);
            },
            ct);

        if (result.FailureCode == CurrentUserProfilePatchFailure.UserNotFound)
            return TypedResults.NotFound();
        if (result.FailureCode == CurrentUserProfilePatchFailure.WallpaperNotUploaded)
        {
            return TypedResults.BadRequest(new CurrentUserProfilePatchFailureResponse(
                CurrentUserProfilePatchFailureCode.WallpaperNotUploaded));
        }
        if (!result.Succeeded)
        {
            return ApiProblems.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ApiMessages.Get(ApiMessageId.PatchMyProfileTitleProfileWasUpdated),
                detail: ApiMessages.For(result.FailureCode));
        }

        return TypedResults.Ok(CurrentUserProfileMapping.ToResponse(
            result.Value!,
            privacy.Value));
    }

    private static CurrentUserProfilePatchSection ResolveSections(
        PatchMyProfileRequest request) =>
        (request.Profile is null ? CurrentUserProfilePatchSection.None
            : CurrentUserProfilePatchSection.Profile)
        | (request.SchoolIdentity is null ? CurrentUserProfilePatchSection.None
            : CurrentUserProfilePatchSection.SchoolIdentity)
        | (request.Appearance is null ? CurrentUserProfilePatchSection.None
            : CurrentUserProfilePatchSection.Appearance);
}
