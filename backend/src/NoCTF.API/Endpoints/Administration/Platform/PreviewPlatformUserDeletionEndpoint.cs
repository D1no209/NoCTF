using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Administration.Platform;

[JsonConverter(typeof(JsonStringEnumConverter<PlatformUserDeletionReferenceCode>))]
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
    UserAccountStatus AccountStatus,
    bool CanHardDelete,
    bool CanAnonymize,
    bool SelfDeletionForbidden,
    bool LastAdministratorProtected,
    IReadOnlyList<PlatformUserDeletionReferenceResponse> References);

internal static class PlatformUserDeletionMapping
{
    public static PlatformUserDeletionPreviewResponse ToResponse(UserDeletionPreview preview) =>
        new(
            preview.UserId,
            preview.UserName,
            preview.AccountStatus,
            preview.CanHardDelete,
            preview.CanAnonymize,
            preview.SelfDeletionForbidden,
            preview.LastAdministratorProtected,
            preview.References.Select(reference => new PlatformUserDeletionReferenceResponse(
                ToCode(reference.Kind),
                reference.Count)).ToArray());

    private static PlatformUserDeletionReferenceCode ToCode(UserDeletionReferenceKind kind) =>
        kind switch
        {
            UserDeletionReferenceKind.CompetitionOwner =>
                PlatformUserDeletionReferenceCode.CompetitionOwner,
            UserDeletionReferenceKind.CompetitionCollaborator =>
                PlatformUserDeletionReferenceCode.CompetitionCollaborator,
            UserDeletionReferenceKind.ChallengeOwner =>
                PlatformUserDeletionReferenceCode.ChallengeOwner,
            UserDeletionReferenceKind.ChallengeManager =>
                PlatformUserDeletionReferenceCode.ChallengeManager,
            UserDeletionReferenceKind.TeamCaptain =>
                PlatformUserDeletionReferenceCode.TeamCaptain,
            UserDeletionReferenceKind.TeamMember =>
                PlatformUserDeletionReferenceCode.TeamMember,
            UserDeletionReferenceKind.Submission =>
                PlatformUserDeletionReferenceCode.Submission,
            UserDeletionReferenceKind.PatchUpload =>
                PlatformUserDeletionReferenceCode.PatchUpload,
            UserDeletionReferenceKind.Notification =>
                PlatformUserDeletionReferenceCode.Notification,
            UserDeletionReferenceKind.ScoringEvent =>
                PlatformUserDeletionReferenceCode.ScoringEvent,
            UserDeletionReferenceKind.CompetitionLifecycleAudit =>
                PlatformUserDeletionReferenceCode.CompetitionLifecycleAudit,
            UserDeletionReferenceKind.CompetitionQuestion =>
                PlatformUserDeletionReferenceCode.CompetitionQuestion,
            UserDeletionReferenceKind.CompetitionQuestionEntry =>
                PlatformUserDeletionReferenceCode.CompetitionQuestionEntry,
            UserDeletionReferenceKind.CompetitionEvent =>
                PlatformUserDeletionReferenceCode.CompetitionEvent,
            UserDeletionReferenceKind.UserAccountLifecycleAudit =>
                PlatformUserDeletionReferenceCode.UserAccountLifecycleAudit,
            _ => throw new InvalidOperationException(
                $"Unsupported user deletion reference kind: {kind}.")
        };
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
