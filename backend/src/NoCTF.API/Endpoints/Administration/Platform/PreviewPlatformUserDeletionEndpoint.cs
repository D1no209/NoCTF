using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Domain.Identity;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.Platform;

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<PlatformUserDeletionReferenceCode>))]
public enum PlatformUserDeletionReferenceCode
{
    CompetitionOwner,
    CompetitionCollaborator,
    ChallengeOwner,
    ChallengeManager,
    TeamCaptain,
    TeamMember,
    Submission,
    PatchUpload,
    Notification,
    ScoringEvent,
    CompetitionLifecycleAudit,
    CompetitionQuestion,
    CompetitionQuestionEntry,
    CompetitionEvent,
    UserAccountLifecycleAudit
}

public sealed record PlatformUserDeletionReferenceResponse(
    PlatformUserDeletionReferenceCode Code,
    int Count);

public sealed record PlatformUserDeletionPreviewResponse(
    Guid UserId,
    string UserName,
    PlatformUserAccountStatusProtocol AccountStatus,
    bool CanHardDelete,
    bool CanAnonymize,
    bool SelfDeletionForbidden,
    bool LastAdministratorProtected,
    IReadOnlyList<PlatformUserDeletionReferenceResponse> References);

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Source)]
internal static partial class PlatformUserDeletionMapping
{
    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial PlatformUserAccountStatusProtocol ToProtocol(UserAccountStatus value);

    public static partial PlatformUserDeletionPreviewResponse ToResponse(UserDeletionPreview preview);

    [MapProperty(nameof(UserDeletionReference.Kind), nameof(PlatformUserDeletionReferenceResponse.Code))]
    public static partial PlatformUserDeletionReferenceResponse ToResponse(UserDeletionReference reference);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial PlatformUserDeletionReferenceCode ToProtocol(UserDeletionReferenceKind value);

}

public sealed class PreviewPlatformUserDeletionEndpoint(
    ManageUserAccounts accounts,
    IUserContext actor)
    : EndpointWithoutRequest<Results<Ok<PlatformUserDeletionPreviewResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/platform/users/{userId}/deletion-preview");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformPreviewUserDeletion"));
        Summary(summary =>
        {
            summary.Summary = "Previews the impact of deleting a platform user.";
            summary.Description =
                "Reports historical references and whether physical deletion or irreversible anonymization is currently allowed.";
        });
    }

    public override async Task<Results<Ok<PlatformUserDeletionPreviewResponse>, NotFound>>
        ExecuteAsync(CancellationToken ct)
    {
        var preview = await accounts.PreviewDeletionAsync(
            Route<Guid>("userId"),
            actor.UserId,
            ct);
        return preview is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(PlatformUserDeletionMapping.ToResponse(preview));
    }
}
 [JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<PlatformUserAccountStatusProtocol>))]
public enum PlatformUserAccountStatusProtocol
{
    Active,
    Banned,
    Disabled,
    Anonymized
}
