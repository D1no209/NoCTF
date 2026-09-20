using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Security;
using NoCTF.Application.Administration;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Domain.Identity;
using Riok.Mapperly.Abstractions;
using NoCTF.API.Serialization;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Platform;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<PlatformManagedUserAccountStatusProtocol>))]
public enum PlatformManagedUserAccountStatusProtocol
{
    Active,
    Banned,
    Disabled
}

public sealed class PatchPlatformUserRequest
{
    public UserRoleProtocol? Role { get; set; }
    public PlatformManagedUserAccountStatusProtocol? AccountStatus { get; set; }
    public bool? EmailVerified { get; set; }
}

public sealed class PatchPlatformUserValidator : Validator<PatchPlatformUserRequest>
{
    public PatchPlatformUserValidator()
    {
        RuleFor(request => request)
            .Must(request => request.Role is not null
                || request.AccountStatus is not null
                || request.EmailVerified is not null)
            .WithMessage("At least one platform-user field is required.");
        RuleFor(request => request.Role).IsInEnum();
        RuleFor(request => request.AccountStatus).IsInEnum();
    }
}

[Mapper(
    AutoUserMappings = false,
    AllowNullPropertyAssignment = false,
    RequiredMappingStrategy = RequiredMappingStrategy.Both)]
public static partial class PlatformUserPatchMapper
{
    [MapProperty(nameof(PatchPlatformUserRequest.AccountStatus), nameof(User.AccountStatus))]
    [MapperIgnoreSource(nameof(PatchPlatformUserRequest.EmailVerified))]
    [MapperIgnoreTarget(nameof(User.Id))]
    [MapperIgnoreTarget(nameof(User.UserName))]
    [MapperIgnoreTarget(nameof(User.NormalizedUserName))]
    [MapperIgnoreTarget(nameof(User.Email))]
    [MapperIgnoreTarget(nameof(User.PasswordHash))]
    [MapperIgnoreTarget(nameof(User.Kind))]
    [MapperIgnoreTarget(nameof(User.TokenVersion))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityProviderId))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityProtocol))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityNamespace))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentitySubject))]
    [MapperIgnoreTarget(nameof(User.ExternalIdentityBoundAt))]
    [MapperIgnoreTarget(nameof(User.Description))]
    [MapperIgnoreTarget(nameof(User.SchoolFullName))]
    [MapperIgnoreTarget(nameof(User.SchoolStudentNumber))]
    [MapperIgnoreTarget(nameof(User.AvatarFileId))]
    [MapperIgnoreTarget(nameof(User.AvatarFile))]
    [MapperIgnoreTarget(nameof(User.WallpaperFileId))]
    [MapperIgnoreTarget(nameof(User.WallpaperFile))]
    [MapperIgnoreTarget(nameof(User.WallpaperEnabled))]
    [MapperIgnoreTarget(nameof(User.EmailVerifiedAt))]
    [MapperIgnoreTarget(nameof(User.CreatedAt))]
    [MapperIgnoreTarget(nameof(User.UpdatedAt))]
    public static partial void ApplyAsPlatformAdministrator(
        PatchPlatformUserRequest request,
        [MappingTarget] User target);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial UserRole ToDomain(UserRoleProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    [MapperIgnoreTargetValue(UserAccountStatus.Anonymized)]
    private static partial UserAccountStatus ToDomain(
        PlatformManagedUserAccountStatusProtocol value);
}

public sealed class PatchPlatformUserEndpoint(
    ManagePlatform platform,
    AccountPrivacy privacy,
    IUserContext actor,
    TimeProvider timeProvider)
    : Endpoint<PatchPlatformUserRequest,
        Results<Ok<PlatformUserDetailResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Patch("/admin/platform/users/{userId}");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformPatchUser"));
        Summary(summary => summary.Summary = "Updates selected platform-user administration fields.");
    }

    public override async Task<Results<Ok<PlatformUserDetailResponse>, NotFound,
        ProblemHttpResult>> ExecuteAsync(
        PatchPlatformUserRequest request,
        CancellationToken ct)
    {
        var userId = Route<Guid>("userId");
        var result = await platform.PatchUserAsync(
            userId,
            actor.UserId,
            target => PlatformUserPatchMapper.ApplyAsPlatformAdministrator(request, target),
            request.EmailVerified,
            timeProvider.GetUtcNow(),
            ct);
        if (result.State == PatchPlatformUserState.UserNotFound)
            return TypedResults.NotFound();
        if (result.State != PatchPlatformUserState.Updated)
            return Failure(result.State.ToString());

        var privateDetails = await privacy.ReadPlatformAsync(actor.UserId, userId, ct);
        if (result.User is null || privateDetails is null)
            return TypedResults.NotFound();
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        return TypedResults.Ok(new PlatformUserDetailResponse(
            PlatformUserMapping.ToResponse(result.User),
            new CurrentUserSchoolIdentityResponse(
                privateDetails.Identity.FullName,
                privateDetails.Identity.StudentNumber)));
    }

    private static ProblemHttpResult Failure(string code) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Platform user was not updated.",
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
