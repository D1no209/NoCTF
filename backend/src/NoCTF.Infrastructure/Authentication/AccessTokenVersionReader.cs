using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.RefreshSession;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;

namespace NoCTF.Infrastructure.Authentication;

public sealed class AccessTokenVersionReader(NoCtfDbContext db) : IAccessTokenVersionReader
{
    public Task<bool> IsCurrentAsync(
        Guid userId,
        int tokenVersion,
        CancellationToken cancellationToken,
        AdministratorIssuedAccessToken? administratorIssuedToken = null)
    {
        var users = db.Users.AsNoTracking().Where(
            user => user.Id == userId
                && user.AccountStatus == NoCTF.Domain.Identity.UserAccountStatus.Active
                && user.TokenVersion == tokenVersion);
        if (administratorIssuedToken is null)
            return users.AnyAsync(cancellationToken);

        var issuedToken = administratorIssuedToken;
        return users.AnyAsync(
            _ => db.Notifications.Any(notification =>
                    notification.Id == issuedToken.JwtId
                    && notification.Kind == NotificationKind.PlatformUserAccessTokenIssued
                    && notification.SourceType == NotificationSourceType.User
                    && notification.SourceId == issuedToken.AdministratorUserId
                    && notification.RelatedType == EntityReferenceKind.User
                    && notification.RelatedId == userId)
                && !db.Notifications.Any(notification =>
                    notification.Kind == NotificationKind.PlatformUserAccessTokenRevoked
                    && notification.RelatedType == EntityReferenceKind.Notification
                    && notification.RelatedId == issuedToken.JwtId),
            cancellationToken);
    }
}
