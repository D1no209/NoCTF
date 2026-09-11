using NoCTF.Application.Common;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Authentication.Account;

public enum CurrentUserProfilePatchFailure
{
    UserNotFound,
    InvalidDescription,
    InvalidSchoolIdentity,
    WallpaperNotUploaded
}

public interface ICurrentUserProfilePatchStore
{
    Task<User?> FindForPatchAsync(Guid userId, CancellationToken cancellationToken);

    Task SaveAsync(User user, CancellationToken cancellationToken);

    void DiscardChanges();
}

public sealed class PatchCurrentUserProfile(
    ICurrentUserProfilePatchStore store,
    TimeProvider timeProvider)
{
    public async Task<OperationResult<User, CurrentUserProfilePatchFailure>> ExecuteAsync(
        Guid userId,
        Action<User> apply,
        CancellationToken cancellationToken = default)
    {
        var user = await store.FindForPatchAsync(userId, cancellationToken);
        if (user is null)
        {
            return OperationResult<User, CurrentUserProfilePatchFailure>.Failure(
                CurrentUserProfilePatchFailure.UserNotFound,
                "User was not found.");
        }

        apply(user);

        if (user.Description?.Length > UserProfileRules.MaximumDescriptionLength)
        {
            store.DiscardChanges();
            return OperationResult<User, CurrentUserProfilePatchFailure>.Failure(
                CurrentUserProfilePatchFailure.InvalidDescription,
                "Description is too long.");
        }

        if (user.SchoolFullName?.Length > 100
            || user.SchoolStudentNumber?.Length > 64
            || user.SchoolFullName?.Any(char.IsControl) == true
            || user.SchoolStudentNumber?.Any(char.IsControl) == true)
        {
            store.DiscardChanges();
            return OperationResult<User, CurrentUserProfilePatchFailure>.Failure(
                CurrentUserProfilePatchFailure.InvalidSchoolIdentity,
                "School identity is invalid.");
        }

        if (user.WallpaperEnabled && user.WallpaperFileId is null)
        {
            store.DiscardChanges();
            return OperationResult<User, CurrentUserProfilePatchFailure>.Failure(
                CurrentUserProfilePatchFailure.WallpaperNotUploaded,
                "A wallpaper must be uploaded before it can be enabled.");
        }

        user.Description = Normalize(user.Description);
        user.SchoolFullName = Normalize(user.SchoolFullName);
        user.SchoolStudentNumber = Normalize(user.SchoolStudentNumber);
        user.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveAsync(user, cancellationToken);
        return OperationResult<User, CurrentUserProfilePatchFailure>.Success(user);
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
