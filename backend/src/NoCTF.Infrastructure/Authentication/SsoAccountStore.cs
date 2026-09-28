using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public enum SsoBindingAuditAction
{
    Bound,
    Unbound,
    AdministrativelyUnbound
}

public sealed record SsoBindingAuditFact(
    int SchemaVersion,
    SsoBindingAuditAction Action,
    Guid UserId,
    Guid ProviderId,
    string ProviderName,
    SsoProtocol Protocol);

public sealed class SsoAccountStore(NoCtfDbContext db) : ISsoAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SsoBindingView?> GetBindingAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == userId
                && item.Kind == UserKind.Human
                && item.AccountStatus == UserAccountStatus.Active,
            ct);
        if (user?.ExternalIdentityProviderId is not Guid providerId
            || user.ExternalIdentityProtocol is not SsoProtocol protocol
            || user.ExternalIdentityNamespace is null
            || user.ExternalIdentitySubject is null
            || user.ExternalIdentityBoundAt is null)
            return null;
        var settings = await db.PlatformSettings.AsNoTracking().AsSplitQuery()
            .SingleAsync(ct);
        var provider = settings.SsoConfiguration.Providers
            .SingleOrDefault(item => item.Id == providerId);
        var providerName = provider?.Name ?? "Unavailable provider";
        return new(
            providerId,
            providerName,
            provider?.IconUrl,
            protocol,
            user.ExternalIdentityNamespace,
            user.ExternalIdentitySubject,
            user.ExternalIdentityBoundAt.Value);
    }

    public async Task<SsoExternalAccountLookup> FindByExternalIdentityAsync(
        SsoExternalIdentity identity,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ExternalIdentity != null
            && item.ExternalIdentity.ProviderId == identity.ProviderId
            && item.ExternalIdentity.Protocol == identity.Protocol
            && item.ExternalIdentity.IdentityNamespace == identity.IdentityNamespace
            && item.ExternalIdentity.Subject == identity.Subject,
            ct);
        if (user is null)
            return new(SsoExternalAccountLookupState.NotLinked);
        if (user.Kind != UserKind.Human || user.AccountStatus != UserAccountStatus.Active)
            return new(SsoExternalAccountLookupState.AccountUnavailable);
        return new(SsoExternalAccountLookupState.Available, new(
            user.Id,
            user.UserName,
            user.Role,
            user.Kind,
            user.TokenVersion,
            user.EmailVerifiedAt is not null));
    }

    public async Task<SsoBindResult> BindAsync(
        Guid userId,
        int tokenVersion,
        SsoExternalIdentity identity,
        string providerName,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : null;
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null || user.Kind != UserKind.Human
            || user.AccountStatus != UserAccountStatus.Active
            || user.TokenVersion != tokenVersion)
            return new(SsoBindState.AccountUnavailable);
        if (user.ExternalIdentityProviderId is not null)
            return new(SsoBindState.AccountAlreadyLinked);
        if (await db.Users.AsNoTracking().AnyAsync(item =>
                item.ExternalIdentity != null
                && item.ExternalIdentity.ProviderId == identity.ProviderId
                && item.ExternalIdentity.Subject == identity.Subject,
                ct))
            return new(SsoBindState.IdentityAlreadyLinked);

        user.ExternalIdentityProviderId = identity.ProviderId;
        user.ExternalIdentityProtocol = identity.Protocol;
        user.ExternalIdentityNamespace = identity.IdentityNamespace;
        user.ExternalIdentitySubject = identity.Subject;
        user.ExternalIdentityBoundAt = now;
        user.UpdatedAt = now;
        db.Notifications.Add(Audit(
            user.Id,
            user.Id,
            identity.ProviderId,
            providerName,
            identity.Protocol,
            SsoBindingAuditAction.Bound,
            now));
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            if (await db.Users.AsNoTracking().AnyAsync(item =>
                    item.ExternalIdentity != null
                    && item.ExternalIdentity.ProviderId == identity.ProviderId
                    && item.ExternalIdentity.Subject == identity.Subject,
                    ct))
                return new(SsoBindState.IdentityAlreadyLinked);
            throw;
        }
        return new(SsoBindState.Bound, new(
            identity.ProviderId,
            providerName,
            null,
            identity.Protocol,
            identity.IdentityNamespace,
            identity.Subject,
            now));
    }

    public async Task<SsoUnbindState> UnbindAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : null;
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null || user.Kind != UserKind.Human
            || user.AccountStatus != UserAccountStatus.Active)
            return SsoUnbindState.AccountUnavailable;
        if (user.ExternalIdentityProviderId is not Guid providerId
            || user.ExternalIdentityProtocol is not SsoProtocol protocol)
            return SsoUnbindState.NotLinked;
        var settings = await db.PlatformSettings.AsNoTracking().AsSplitQuery()
            .SingleAsync(ct);
        var providerName = settings.SsoConfiguration.Providers
            .SingleOrDefault(provider => provider.Id == providerId)?.Name
            ?? "Unavailable provider";
        user.ExternalIdentityProviderId = null;
        user.ExternalIdentityProtocol = null;
        user.ExternalIdentityNamespace = null;
        user.ExternalIdentitySubject = null;
        user.ExternalIdentityBoundAt = null;
        user.TokenVersion = checked(user.TokenVersion + 1);
        user.UpdatedAt = now;
        db.Notifications.Add(Audit(
            user.Id,
            user.Id,
            providerId,
            providerName,
            protocol,
            SsoBindingAuditAction.Unbound,
            now));
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return SsoUnbindState.Unbound;
    }

    public async Task<AdminSsoUnbindState> UnbindAsAdministratorAsync(
        Guid userId,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : null;
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null || user.Kind != UserKind.Human)
            return AdminSsoUnbindState.UserNotFound;
        if (user.ExternalIdentityProviderId is not Guid providerId
            || user.ExternalIdentityProtocol is not SsoProtocol protocol)
            return AdminSsoUnbindState.NotLinked;
        var settings = await db.PlatformSettings.AsNoTracking().AsSplitQuery()
            .SingleAsync(ct);
        var providerName = settings.SsoConfiguration.Providers
            .SingleOrDefault(provider => provider.Id == providerId)?.Name
            ?? "Unavailable provider";
        user.ExternalIdentityProviderId = null;
        user.ExternalIdentityProtocol = null;
        user.ExternalIdentityNamespace = null;
        user.ExternalIdentitySubject = null;
        user.ExternalIdentityBoundAt = null;
        user.TokenVersion = checked(user.TokenVersion + 1);
        user.UpdatedAt = now;
        db.Notifications.Add(Audit(
            actorUserId,
            user.Id,
            providerId,
            providerName,
            protocol,
            SsoBindingAuditAction.AdministrativelyUnbound,
            now));
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return AdminSsoUnbindState.Unbound;
    }

    private static Notification Audit(
        Guid actorUserId,
        Guid userId,
        Guid providerId,
        string providerName,
        SsoProtocol protocol,
        SsoBindingAuditAction action,
        DateTimeOffset now) => new SsoExternalIdentityBindingChangedNotification
    {
        Id = Guid.CreateVersion7(now),
        SourceType = NotificationSourceType.User,
        SourceId = actorUserId,
        TargetType = NotificationTargetType.PlatformAdministrators,
        TargetId = Notification.PlatformAdministratorsTargetId,
        ActionValue = (int)action,
        UserId = userId,
        SsoProviderId = providerId,
        ProviderName = providerName,
        SsoProtocol = protocol,
        SentAt = now,
        RelatedType = EntityReferenceKind.User,
        RelatedId = userId
    };
}
