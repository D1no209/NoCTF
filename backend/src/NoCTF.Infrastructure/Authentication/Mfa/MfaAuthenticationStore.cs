using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Domain.Platform;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Admission;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication.Mfa;

public sealed partial class MfaAuthenticationStore(NoCtfDbContext db, IMfaCryptography crypto,
    PlatformSecretProtector protector, TimeProvider clock, IRequestSourceAddress? source = null,
    NoCTF.Application.Messaging.IPostCommitMessagePublisher? publisher = null) : IMfaAuthenticationStore, IMfaManagementStore
{
    public async Task<MfaAccountSnapshot?> ReadAccountAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(value => value.Id == userId && value.AccountStatus == UserAccountStatus.Active, ct);
        if (user is null) return null;
        var settings = await SettingsAsync(ct);
        var credential = await db.UserTotpCredentials.AsNoTracking().SingleOrDefaultAsync(value => value.UserId == userId, ct);
        var privileged = await CompetitionPrivilegedAsync(userId, ct);
        var count = credential is null ? 0 : await db.UserMfaRecoveryCodes.CountAsync(value => value.UserId == userId && value.BatchId == credential.RecoveryBatchId && value.ConsumedAt == null, ct);
        return new(ToAuthenticated(user), MfaRequirements.IsMandated(user.Kind, user.Role, user.MfaRequired, settings.MfaPolicy, privileged),
            MfaRequirements.IsRequired(user.Kind, user.Role, user.MfaRequired, settings.MfaPolicy, privileged, credential is not null),
            settings.MfaPolicyStamp, credential?.Id, count, user.EmailVerifiedAt is not null && MailConfigured(settings));
    }

    public async Task<MfaFailure?> ValidateContextAsync(Guid userId, int tokenVersion, AuthenticationContext? context, CancellationToken ct)
    {
        var account = await ReadAccountAsync(userId, ct);
        if (account is null || account.User.TokenVersion != tokenVersion) return MfaFailure.AccountUnavailable;
        if (account.User.Kind == UserKind.Bot) return null;
        var now = clock.GetUtcNow();
        if (context is null || !context.IsWellFormed || context.Method == AuthenticationMethod.Bot || context.AuthenticatedAt > now.AddSeconds(30)) return MfaFailure.PrimaryAuthenticationRequired;
        if (context.HasMfa)
        {
            if (context.MfaAuthenticatedAt > now.AddSeconds(30) || context.MfaDeadline <= now) return MfaFailure.PrimaryAuthenticationRequired;
            if (context.IsLocalMfa && context.CredentialId != account.CredentialId) return MfaFailure.PrimaryAuthenticationRequired;
            if (context.MfaSource == MfaSource.Oidc && !await OidcPolicyCurrentAsync(context.ProviderId!.Value, context.TrustPolicyId!.Value, ct)) return MfaFailure.PrimaryAuthenticationRequired;
        }
        if (account.Required && !context.HasMfa)
            return account.CredentialId is null ? MfaFailure.NotEnrolled : MfaFailure.LocalMfaRequired;
        return null;
    }

    public async Task<bool> IsOidcProofCurrentAsync(OidcMfaProof proof, CancellationToken ct)
    {
        var provider = await db.Set<OidcSsoProviderConfiguration>().AsNoTracking().IgnoreAutoIncludes().SingleOrDefaultAsync(value => value.Id == proof.ProviderId, ct);
        return provider is not null && proof.AuthenticatedAt <= clock.GetUtcNow().AddSeconds(30)
            && clock.GetUtcNow() - proof.AuthenticatedAt <= TimeSpan.FromSeconds(provider.MfaAuthenticationMaxAgeSeconds)
            && await OidcPolicyCurrentAsync(proof.ProviderId, proof.TrustPolicyId, ct);
    }

    private async Task<string?> ProviderFingerprintAsync(Guid providerId, CancellationToken ct)
    {
        var settings = await SettingsAsync(ct);
        if (!settings.SsoEnabled) return null;
        var provider = settings.SsoConfiguration.Providers.SingleOrDefault(value => value.Id == providerId && value.Enabled && value.AllowLogin);
        return provider is null ? null : SsoProviderRuntimeReader.Fingerprint(provider, settings.SsoConfiguration.PublicBaseUrl)
            + (provider.Oidc?.MfaTrustPolicyId.ToString("N") ?? string.Empty);
    }

    private async Task<bool> OidcPolicyCurrentAsync(Guid providerId, Guid policyId, CancellationToken ct) =>
        await db.PlatformSettings.AsNoTracking().IgnoreAutoIncludes().AnyAsync(value => value.Id == 1 && value.SsoEnabled, ct)
        && await db.Set<OidcSsoProviderConfiguration>().AsNoTracking().IgnoreAutoIncludes().AnyAsync(value => value.Id == providerId && value.Enabled && value.AllowLogin
            && value.MfaTrustEnabled && value.MfaTrustPolicyId == policyId && policyId != Guid.Empty, ct);

    public async Task<(MfaFlowView Flow, MfaBrowserCredential Browser)> CreateFlowAsync(PrimaryAuthentication primary, MfaChallengePurpose purpose, CancellationToken ct)
    {
        if (primary.User.Kind != UserKind.Human || primary.Method is not (AuthenticationMethod.Password or AuthenticationMethod.Oidc or AuthenticationMethod.Cas))
            throw new InvalidOperationException("Interactive authentication is required.");
        var account = await ReadAccountAsync(primary.User.Id, ct) ?? throw new InvalidOperationException("Account is unavailable.");
        if (account.User.TokenVersion != primary.User.TokenVersion) throw new InvalidOperationException("Primary authentication is no longer current.");
        var secret = crypto.GenerateBrowserSecret(); var now = clock.GetUtcNow();
        var challenge = new MfaChallenge { Id = Guid.NewGuid(), UserId = primary.User.Id, Purpose = purpose,
            BrowserBindingHash = crypto.HashBrowserSecret(secret), CreatedAt = now,
            ExpiresAt = now.AddMinutes(purpose is MfaChallengePurpose.Enrollment or MfaChallengePurpose.Rebind or MfaChallengePurpose.RecoveryEnrollment ? 10 : 5),
            TokenVersion = account.User.TokenVersion, PolicyStamp = account.PolicyStamp, CredentialId = account.CredentialId,
            PrimaryMethod = primary.Method, PrimaryAuthenticatedAt = primary.AuthenticatedAt, PrimaryProviderId = primary.ProviderId,
            ReturnPath = SafeReturnPath(primary.ReturnPath),
            PrimaryProviderFingerprint = primary.ProviderId is { } providerId ? await ProviderFingerprintAsync(providerId, ct) : null };
        db.MfaChallenges.Add(challenge); await db.SaveChangesAsync(ct);
        return (View(challenge, account.User.UserName, account.RecoveryCodesRemaining > 0), new(challenge.Id, secret));
    }

    public async Task<OperationResult<MfaFlowView, MfaFailure>> ReadFlowAsync(MfaBrowserCredential browser, CancellationToken ct)
    {
        var flow = await FindFlowAsync(browser, ct);
        if (flow.Failure is { } error) return Failure<MfaFlowView>(error);
        var challenge = flow.Challenge!; var user = flow.User!;
        string? secret = null; string? uri = null;
        if (challenge.PendingSecretCiphertext is not null)
        {
            secret = protector.Unprotect(challenge.PendingSecretCiphertext, PlatformSecretPurpose.PendingTotpSecret, user.Id, challenge.Id);
            uri = ProvisioningUri((await SettingsAsync(ct)).Name, user.UserName, secret);
        }
        var credential = await db.UserTotpCredentials.AsNoTracking().SingleOrDefaultAsync(value => value.UserId == user.Id, ct);
        var recovery = credential is not null && await db.UserMfaRecoveryCodes.AnyAsync(value => value.UserId == user.Id && value.BatchId == credential.RecoveryBatchId && value.ConsumedAt == null, ct);
        return Success(View(challenge, user.UserName, recovery, secret, uri));
    }

    public async Task CancelFlowAsync(MfaBrowserCredential browser, CancellationToken ct)
    {
        var flow = await FindFlowAsync(browser, ct);
        if (flow.Challenge is null || flow.Failure is not null) return;
        flow.Challenge.State = MfaChallengeState.Cancelled;
        flow.Challenge.PendingSecretCiphertext = null;
        await db.SaveChangesAsync(ct);
    }

    public Task<OperationResult<MfaAuthenticationResult, MfaFailure>> VerifyAsync(MfaBrowserCredential browser, MfaVerification verification, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            var flow = await FindFlowAsync(browser, ct);
            if (flow.Failure is { } error) return Failure<MfaAuthenticationResult>(error);
            var challenge = flow.Challenge!; var user = flow.User!;
            if (challenge.Purpose != MfaChallengePurpose.Login) return Failure<MfaAuthenticationResult>(MfaFailure.NotApplicable);
            var credential = await db.UserTotpCredentials.SingleOrDefaultAsync(value => value.UserId == user.Id, ct);
            if (credential is null || challenge.CredentialId != credential.Id) return Failure<MfaAuthenticationResult>(MfaFailure.PolicyChanged);
            var failure = await VerifyFactorAsync(user, credential, verification, ct);
            if (failure is not null) return await FailedAsync<MfaAuthenticationResult>(challenge, user.Id, failure.Value, ct);
            challenge.State = MfaChallengeState.Completed;
            var context = new AuthenticationContext(challenge.PrimaryMethod, challenge.PrimaryAuthenticatedAt,
                verification.Method == MfaVerificationMethod.Totp ? MfaSource.Totp : MfaSource.RecoveryCode, clock.GetUtcNow(), credential.Id);
            return Success(new MfaAuthenticationResult(ToAuthenticated(user), context, challenge.ReturnPath));
        }, ct);

    public Task<OperationResult<MfaFlowView, MfaFailure>> BeginEnrollmentAsync(MfaBrowserCredential browser, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            var flow = await FindFlowAsync(browser, ct);
            if (flow.Failure is { } error) return Failure<MfaFlowView>(error);
            var challenge = flow.Challenge!; var user = flow.User!;
            if (challenge.Purpose is not (MfaChallengePurpose.Enrollment or MfaChallengePurpose.Rebind or MfaChallengePurpose.RecoveryEnrollment)) return Failure<MfaFlowView>(MfaFailure.NotApplicable);
            if (challenge.PrimaryAuthenticatedAt == default) return Failure<MfaFlowView>(MfaFailure.PrimaryAuthenticationRequired);
            if (challenge.Purpose == MfaChallengePurpose.Enrollment && await db.UserTotpCredentials.AnyAsync(value => value.UserId == user.Id, ct)) return Failure<MfaFlowView>(MfaFailure.AlreadyEnrolled);
            var secret = challenge.PendingSecretCiphertext is null ? crypto.GenerateSecret()
                : protector.Unprotect(challenge.PendingSecretCiphertext, PlatformSecretPurpose.PendingTotpSecret, user.Id, challenge.Id);
            challenge.PendingCredentialId ??= Guid.NewGuid();
            challenge.PendingSecretCiphertext ??= protector.Protect(secret, PlatformSecretPurpose.PendingTotpSecret, user.Id, challenge.Id);
            return Success(View(challenge, user.UserName, false, secret, ProvisioningUri((await SettingsAsync(ct)).Name, user.UserName, secret)));
        }, ct);

    public Task<OperationResult<MfaAuthenticationResult, MfaFailure>> ConfirmEnrollmentAsync(MfaBrowserCredential browser, string code, CancellationToken ct, bool accountManagement = false) =>
        TransactionAsync(async () =>
        {
            var flow = await FindFlowAsync(browser, ct);
            if (flow.Failure is { } error) return Failure<MfaAuthenticationResult>(error);
            var challenge = flow.Challenge!; var user = flow.User!;
            if (challenge.PrimaryAuthenticatedAt == default) return Failure<MfaAuthenticationResult>(MfaFailure.PrimaryAuthenticationRequired);
            if (accountManagement != (challenge.Operation is MfaOperation.EnableTotp or MfaOperation.RebindTotp)) return Failure<MfaAuthenticationResult>(MfaFailure.NotApplicable);
            if (challenge.Purpose is not (MfaChallengePurpose.Enrollment or MfaChallengePurpose.Rebind or MfaChallengePurpose.RecoveryEnrollment)
                || challenge.PendingSecretCiphertext is null || challenge.PendingCredentialId is null) return Failure<MfaAuthenticationResult>(MfaFailure.NotApplicable);
            if (await FailureBudgetExceededAsync(user.Id, ct)) return Failure<MfaAuthenticationResult>(MfaFailure.RateLimited);
            var secret = protector.Unprotect(challenge.PendingSecretCiphertext, PlatformSecretPurpose.PendingTotpSecret, user.Id, challenge.Id);
            if (!crypto.VerifyTotp(secret, code, clock.GetUtcNow(), out var step)) return await FailedAsync<MfaAuthenticationResult>(challenge, user.Id, MfaFailure.InvalidCode, ct);
            var old = await db.UserTotpCredentials.SingleOrDefaultAsync(value => value.UserId == user.Id, ct);
            if (challenge.Purpose == MfaChallengePurpose.Enrollment && old is not null) return Failure<MfaAuthenticationResult>(MfaFailure.AlreadyEnrolled);
            if (challenge.Purpose == MfaChallengePurpose.Rebind && old?.Id != challenge.CredentialId) return Failure<MfaAuthenticationResult>(MfaFailure.PolicyChanged);
            if (old is not null) db.UserTotpCredentials.Remove(old);
            // The ordinary unique constraint remains valid during replacement.
            if (old is not null) await db.SaveChangesAsync(ct);
            var credential = new UserTotpCredential { Id = challenge.PendingCredentialId.Value, UserId = user.Id,
                SecretCiphertext = protector.Protect(secret, PlatformSecretPurpose.TotpCredentialSecret, user.Id, challenge.PendingCredentialId.Value),
                EnabledAt = clock.GetUtcNow(), LastAcceptedStep = step, RecoveryBatchId = Guid.NewGuid() };
            db.UserTotpCredentials.Add(credential);
            var codes = NewRecoveryCodes(user.Id, credential.RecoveryBatchId);
            user.TokenVersion = checked(user.TokenVersion + 1);
            challenge.State = MfaChallengeState.Completed; challenge.PendingSecretCiphertext = null; challenge.MailState = MfaMailState.Pending;
            SecurityEvent(user.Id, challenge.Purpose == MfaChallengePurpose.Enrollment ? AccountActivityKind.MfaEnabled
                : challenge.Purpose == MfaChallengePurpose.RecoveryEnrollment ? AccountActivityKind.MfaRecovered : AccountActivityKind.MfaRebound);
            return Success(new MfaAuthenticationResult(ToAuthenticated(user), new(challenge.PrimaryMethod, challenge.PrimaryAuthenticatedAt,
                MfaSource.Totp, clock.GetUtcNow(), credential.Id), challenge.ReturnPath, codes));
        }, ct);

    private async Task<MfaFailure?> VerifyFactorAsync(User user, UserTotpCredential credential, MfaVerification verification, CancellationToken ct)
    {
        if (await FailureBudgetExceededAsync(user.Id, ct)) return MfaFailure.RateLimited;
        if (verification.Method == MfaVerificationMethod.Totp)
        {
            var secret = protector.Unprotect(credential.SecretCiphertext, PlatformSecretPurpose.TotpCredentialSecret, user.Id, credential.Id);
            if (!crypto.VerifyTotp(secret, verification.Code, clock.GetUtcNow(), out var step)) return MfaFailure.InvalidCode;
            if (step <= credential.LastAcceptedStep) return MfaFailure.CodeAlreadyUsed;
            credential.LastAcceptedStep = step; return null;
        }
        if (verification.Method != MfaVerificationMethod.RecoveryCode) return MfaFailure.InvalidCode;
        var hash = crypto.HashRecoveryCode(verification.Code);
        if (hash is null) return MfaFailure.InvalidCode;
        var recovery = await db.UserMfaRecoveryCodes.SingleOrDefaultAsync(value => value.UserId == user.Id && value.BatchId == credential.RecoveryBatchId
            && value.CodeSha256 == hash && value.ConsumedAt == null, ct);
        if (recovery is null) return MfaFailure.InvalidCode;
        recovery.ConsumedAt = clock.GetUtcNow(); return null;
    }

    private IReadOnlyList<string> NewRecoveryCodes(Guid userId, Guid batchId)
    {
        var codes = crypto.GenerateRecoveryCodes();
        db.UserMfaRecoveryCodes.AddRange(codes.Select(value => new UserMfaRecoveryCode { Id = Guid.NewGuid(), UserId = userId,
            BatchId = batchId, CodeSha256 = crypto.HashRecoveryCode(value)!, CreatedAt = clock.GetUtcNow() }));
        return codes;
    }

    private async Task<(MfaChallenge? Challenge, User? User, MfaFailure? Failure)> FindFlowAsync(MfaBrowserCredential browser, CancellationToken ct)
    {
        var challenge = await db.MfaChallenges.SingleOrDefaultAsync(value => value.Id == browser.ChallengeId, ct);
        if (challenge is null || challenge.State is not (MfaChallengeState.Pending or MfaChallengeState.Verified) || challenge.ExpiresAt <= clock.GetUtcNow()) return (null, null, MfaFailure.FlowExpired);
        if (challenge.BrowserBindingHash != crypto.HashBrowserSecret(browser.Secret)) return (null, null, MfaFailure.InvalidBrowser);
        if (challenge.FailedAttempts >= 5) return (null, null, MfaFailure.AttemptsExceeded);
        var user = await db.Users.SingleOrDefaultAsync(value => value.Id == challenge.UserId, ct);
        if (user is null || user.Kind != UserKind.Human || user.AccountStatus != UserAccountStatus.Active || user.TokenVersion != challenge.TokenVersion) return (null, null, MfaFailure.AccountUnavailable);
        if (challenge.PolicyStamp != (await SettingsAsync(ct)).MfaPolicyStamp) return (null, null, MfaFailure.PolicyChanged);
        if (challenge.PrimaryProviderId is { } provider && (challenge.PrimaryProviderFingerprint is null || challenge.PrimaryProviderFingerprint != await ProviderFingerprintAsync(provider, ct))) return (null, null, MfaFailure.PolicyChanged);
        return (challenge, user, null);
    }

    private async Task<bool> FailureBudgetExceededAsync(Guid userId, CancellationToken ct)
    {
        var accountKey = FailureKey("account", userId.ToString("N")); var ipKey = FailureKey("ip", source?.Address ?? "unknown");
        return await db.RequestAdmissionWindows.AnyAsync(value => value.ExpiresAt > clock.GetUtcNow()
            && (value.KeyHash == accountKey && value.Count >= 10 || value.KeyHash == ipKey && value.Count >= 100), ct);
    }

    private async Task<OperationResult<T, MfaFailure>> FailedAsync<T>(MfaChallenge challenge, Guid userId, MfaFailure failure, CancellationToken ct)
    {
        if (failure == MfaFailure.RateLimited) return Failure<T>(failure);
        challenge.FailedAttempts++;
        if (challenge.FailedAttempts >= 5) { challenge.State = MfaChallengeState.Failed; challenge.PendingSecretCiphertext = null; }
        foreach (var key in new[] { FailureKey("account", userId.ToString("N")), FailureKey("ip", source?.Address ?? "unknown") })
        {
            var window = await db.RequestAdmissionWindows.SingleOrDefaultAsync(value => value.KeyHash == key, ct);
            if (window is null) { window = new RequestAdmissionWindow { KeyHash = key }; db.RequestAdmissionWindows.Add(window); }
            if (window.ExpiresAt <= clock.GetUtcNow()) { window.Count = 0; window.ExpiresAt = clock.GetUtcNow().AddMinutes(15); }
            window.Count++;
        }
        return Failure<T>(failure);
    }

    private async Task<OperationResult<T, MfaFailure>> TransactionAsync<T>(Func<Task<OperationResult<T, MfaFailure>>> action, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            db.ChangeTracker.Clear();
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await action();
                db.ChangeTracker.DetectChanges();
                var users = db.ChangeTracker.Entries<User>().Where(value => value.State == EntityState.Modified && value.Property(user => user.TokenVersion).IsModified).Select(value => value.Entity.Id).ToArray();
                var global = db.ChangeTracker.Entries<PlatformSettings>().Any(value => value.State == EntityState.Modified && value.Property(settings => settings.MfaPolicyStamp).IsModified)
                    || db.ChangeTracker.Entries<OidcSsoProviderConfiguration>().Any(value => value.State == EntityState.Modified && value.Property(settings => settings.MfaTrustPolicyId).IsModified);
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
                // Publication is outside the database retry boundary; connection and pending-mail scanners cover a lost wakeup.
                if (publisher is not null && result.Succeeded)
                {
                    try
                    {
                        if (global) await publisher.PublishAsync(new NoCTF.Application.Messaging.MfaAuthenticationChanged(null));
                        else foreach (var userId in users) await publisher.PublishAsync(new NoCTF.Application.Messaging.MfaAuthenticationChanged(userId));
                        var mail = db.ChangeTracker.Entries<MfaChallenge>().Where(value => value.Entity.MailState == MfaMailState.Pending).Select(value => value.Entity.Id).ToArray();
                        foreach (var id in mail) await publisher.PublishAsync(new NoCTF.Application.Messaging.SendMfaMail(id));
                    }
                    catch (Exception) when (!ct.IsCancellationRequested) { /* Authoritative state is committed and will be redispatched. */ }
                }
                return result;
            }
            catch (Exception exception) when (attempt < 2 && (exception is DbUpdateException || TransactionFailureClassifier.IsRetryable(exception)))
            { db.ChangeTracker.Clear(); await Task.Delay(10, ct); }
            catch (Exception exception) when (exception is DbException or DbUpdateException or CryptographicException or InvalidOperationException)
            { db.ChangeTracker.Clear(); return Failure<T>(MfaFailure.DependencyUnavailable); }
        }
        return Failure<T>(MfaFailure.DependencyUnavailable);
    }

    private Task<PlatformSettings> SettingsAsync(CancellationToken ct) => db.PlatformSettings.IgnoreAutoIncludes().SingleAsync(value => value.Id == 1, ct);
    private Task<bool> CompetitionPrivilegedAsync(Guid userId, CancellationToken ct) => db.Competitions.AsNoTracking().AnyAsync(value => value.DeletedAt == null
        && (value.OwnerId == userId || value.Collaborators.Any(member => member.UserId == userId && (member.Role == CompetitionCollaboratorRole.Manager || member.Role == CompetitionCollaboratorRole.Judge))), ct);
    private static AuthenticatedUser ToAuthenticated(User user) => new(user.Id, user.UserName, user.Role, user.Kind, user.TokenVersion, user.EmailVerifiedAt is not null);
    private static bool MailConfigured(PlatformSettings settings) => settings.EmailSmtpHost.Length > 0 && settings.EmailSmtpFromAddress.Length > 0
        && settings.EmailSmtpPort > 0 && settings.EmailSmtpSecurityMode is not null && Uri.TryCreate(settings.EmailPublicBaseUrl, UriKind.Absolute, out _);
    private static string FailureKey(string kind, string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"rate:mfa-failure:{kind}:{value}")));
    private static string SafeReturnPath(string path) => path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains('\\') ? path : "/";
    private static string ProvisioningUri(string issuer, string account, string secret) => $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";
    private static MfaFlowView View(MfaChallenge challenge, string name, bool recovery, string? secret = null, string? uri = null) =>
        new(challenge.Id, challenge.Purpose, challenge.ExpiresAt, Math.Max(0, 5 - challenge.FailedAttempts), name, recovery, challenge.ReturnPath, secret, uri, challenge.PrimaryAuthenticatedAt == default);
    private static OperationResult<T, MfaFailure> Failure<T>(MfaFailure failure) => OperationResult<T, MfaFailure>.Failure(failure, "MFA operation could not be completed.");
    private static OperationResult<T, MfaFailure> Success<T>(T value) => OperationResult<T, MfaFailure>.Success(value);
    private void SecurityEvent(Guid userId, AccountActivityKind action, Guid? actor = null)
    {
        var notification = Privacy.AuthenticationActivity.Create(actor ?? userId, action, source?.Address, clock.GetUtcNow());
        notification.UserId = userId; notification.ActorUserId = actor; notification.TargetType = NoCTF.Domain.Notifications.NotificationTargetType.User;
        notification.TargetId = userId; db.Notifications.Add(notification);
    }
}
