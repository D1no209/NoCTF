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
    Unbound
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
        var settings = await db.PlatformSettings.AsNoTracking().SingleAsync(ct);
        var providerName = settings.SsoConfiguration.Providers
            .SingleOrDefault(provider => provider.Id == providerId)?.Name
            ?? "Unavailable provider";
        return new(
            providerId,
            providerName,
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
            item.ExternalIdentityProviderId == identity.ProviderId
            && item.ExternalIdentityProtocol == identity.Protocol
            && item.ExternalIdentityNamespace == identity.IdentityNamespace
            && item.ExternalIdentitySubject == identity.Subject,
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
        if (db.Database.IsRelational())
        {
            var identityKey = $"{identity.ProviderId:N}\n{identity.Subject}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({identityKey}, 0))",
                ct);
        }
        var user = db.Database.IsRelational()
            ? await db.Users.FromSqlInterpolated(
                    $"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
                .SingleOrDefaultAsync(ct)
            : await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null || user.Kind != UserKind.Human
            || user.AccountStatus != UserAccountStatus.Active
            || user.TokenVersion != tokenVersion)
            return new(SsoBindState.AccountUnavailable);
        if (user.ExternalIdentityProviderId is not null)
            return new(SsoBindState.AccountAlreadyLinked);
        if (await db.Users.AsNoTracking().AnyAsync(item =>
                item.ExternalIdentityProviderId == identity.ProviderId
                && item.ExternalIdentitySubject == identity.Subject,
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
                    item.ExternalIdentityProviderId == identity.ProviderId
                    && item.ExternalIdentitySubject == identity.Subject,
                    ct))
                return new(SsoBindState.IdentityAlreadyLinked);
            throw;
        }
        return new(SsoBindState.Bound, new(
            identity.ProviderId,
            providerName,
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
        var user = db.Database.IsRelational()
            ? await db.Users.FromSqlInterpolated(
                    $"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
                .SingleOrDefaultAsync(ct)
            : await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null || user.Kind != UserKind.Human
            || user.AccountStatus != UserAccountStatus.Active)
            return SsoUnbindState.AccountUnavailable;
        if (user.ExternalIdentityProviderId is not Guid providerId
            || user.ExternalIdentityProtocol is not SsoProtocol protocol)
            return SsoUnbindState.NotLinked;
        var settings = await db.PlatformSettings.AsNoTracking().SingleAsync(ct);
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

    private static Notification Audit(
        Guid userId,
        Guid providerId,
        string providerName,
        SsoProtocol protocol,
        SsoBindingAuditAction action,
        DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        SourceType = NotificationSourceType.User,
        SourceId = userId,
        TargetType = NotificationTargetType.PlatformAdministrators,
        TargetId = Notification.PlatformAdministratorsTargetId,
        Kind = NotificationKind.SsoExternalIdentityBindingChanged,
        ContentJson = JsonSerializer.Serialize(new SsoBindingAuditFact(
            1, action, userId, providerId, providerName, protocol), JsonOptions),
        SentAt = now,
        RelatedType = EntityReferenceKind.User,
        RelatedId = userId
    };
}
