using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.Authentication.Passkeys;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Application.Common;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Domain.Identity.Passkeys;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication.Passkeys;

public sealed class PasskeyStore(NoCtfDbContext db, IPasskeyProtocol protocol, IOptions<PasskeyOptions> configured,
    PlatformSecretProtector protector, IMfaCryptography crypto, IMfaAuthenticationStore mfa,
    IMfaSensitiveOperationProof proofs, TimeProvider clock, IPostCommitMessagePublisher? publisher = null,
    IRequestSourceAddress? source = null) : IPasskeyStore
{
    private PasskeyOptions Options => configured.Value;
    public bool AvailableForOrigin(string origin) => Options.AllowsOrigin(origin);
    public Task<OperationResult<PasskeyStartedCeremony, PasskeyFailure>> BeginLoginAsync(string origin, string returnPath, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            if (!AvailableForOrigin(origin)) return Failure<PasskeyStartedCeremony>(PasskeyFailure.Unavailable);
            var options = await protocol.CreateLoginOptionsAsync(origin, ct);
            return Success(await CreateCeremonyAsync(null, null, PasskeyCeremonyPurpose.Login, options, origin, null, returnPath, ct));
        }, ct);

    public async Task<OperationResult<PasskeyAccountStatus, PasskeyFailure>> ReadAccountAsync(MfaActor actor, string origin, CancellationToken ct)
    {
        var account = await mfa.ReadAccountAsync(actor.UserId, ct);
        if (account is null || account.User.Kind != UserKind.Human || account.User.TokenVersion != actor.TokenVersion)
            return Failure<PasskeyAccountStatus>(PasskeyFailure.AccountUnavailable);
        var credentials = await db.UserPasskeys.AsNoTracking().Where(value => value.UserId == actor.UserId)
            .OrderBy(value => value.CreatedAt).Select(value => new PasskeyAccountCredential(value.Id, value.Name, value.CreatedAt,
                value.LastUsedAt, value.IsBackedUp, value.IsBackupEligible)).ToArrayAsync(ct);
        return Success(new PasskeyAccountStatus(AvailableForOrigin(origin), account.Required,
            RecentPrimary(actor), Options.MaximumCredentials, credentials));
    }

    public Task<OperationResult<PasskeyStartedCeremony, PasskeyFailure>> BeginRegistrationAsync(MfaActor actor, MfaBrowserCredential? proof,
        string name, string origin, CancellationToken ct) => TransactionAsync(async () =>
        {
            if (!AvailableForOrigin(origin)) return Failure<PasskeyStartedCeremony>(PasskeyFailure.Unavailable);
            if (!ValidName(name)) return Failure<PasskeyStartedCeremony>(PasskeyFailure.InvalidName);
            var user = await UserAsync(actor, ct); if (user is null) return Failure<PasskeyStartedCeremony>(PasskeyFailure.AccountUnavailable);
            if (await db.UserPasskeys.CountAsync(value => value.UserId == user.Id, ct) >= Options.MaximumCredentials)
                return Failure<PasskeyStartedCeremony>(PasskeyFailure.CredentialLimitReached);
            var allowed = await AuthorizeChangeAsync(actor, proof, MfaOperation.AddPasskey, user.Id, ct);
            if (allowed is not null) return Failure<PasskeyStartedCeremony>(allowed.Value);
            var options = await protocol.CreateRegistrationOptionsAsync(new(user.Id, user.UserName), origin, ct);
            var required = (await mfa.ReadAccountAsync(user.Id, ct))!.Required;
            return Success(await CreateCeremonyAsync(user.Id, user.TokenVersion, PasskeyCeremonyPurpose.Registration,
                options, origin, name.Trim(), "/", ct, localProofSatisfied: required));
        }, ct);

    public Task<OperationResult<PasskeyAccountCredential, PasskeyFailure>> FinishRegistrationAsync(MfaActor actor,
        PasskeyBrowserCredential browser, string json, string origin, CancellationToken ct) => TransactionAsync(async () =>
        {
            var ceremony = await CeremonyAsync(browser, PasskeyCeremonyPurpose.Registration, origin, ct);
            if (ceremony.FailureCode is { } failure) return Failure<PasskeyAccountCredential>(failure);
            var row = ceremony.Value!; var user = await UserAsync(actor, ct);
            if (user is null || row.UserId != user.Id || row.TokenVersion != user.TokenVersion)
                return Failure<PasskeyAccountCredential>(PasskeyFailure.AccountUnavailable);
            if ((await mfa.ReadAccountAsync(user.Id, ct))!.Required && !row.LocalProofSatisfied)
                return Failure<PasskeyAccountCredential>(PasskeyFailure.PolicyChanged);
            var verified = await protocol.VerifyRegistrationAsync(State(row), json, origin, ct);
            Consume(row, verified.Succeeded);
            if (!verified.Succeeded) return Failure<PasskeyAccountCredential>(verified.FailureCode!.Value);
            if (verified.Value!.UserId != user.Id) return Failure<PasskeyAccountCredential>(PasskeyFailure.InvalidCredential);
            if (await db.UserPasskeys.AnyAsync(value => value.CredentialId == verified.Value.Credential.CredentialId, ct))
                return Failure<PasskeyAccountCredential>(PasskeyFailure.AlreadyRegistered);
            if (await db.UserPasskeys.CountAsync(value => value.UserId == user.Id, ct) >= Options.MaximumCredentials)
                return Failure<PasskeyAccountCredential>(PasskeyFailure.CredentialLimitReached);
            var value = verified.Value.Credential; var id = Guid.NewGuid();
            var credential = new UserPasskey { Id = id, UserId = user.Id, CredentialId = value.CredentialId, PublicKey = value.PublicKey,
                Name = row.Name!, RelyingPartyId = Options.ServerDomain, SignCount = value.SignCount, IsUserVerified = value.IsUserVerified,
                IsBackupEligible = value.IsBackupEligible, IsBackedUp = value.IsBackedUp, AttestationObject = value.AttestationObject,
                ClientDataJson = value.ClientDataJson, CreatedAt = clock.GetUtcNow(), Transports = value.Transports.Select((transport, index) =>
                    new UserPasskeyTransport { UserPasskeyId = id, Position = index, Transport = transport }).ToList() };
            db.UserPasskeys.Add(credential); user.TokenVersion = checked(user.TokenVersion + 1);
            Audit(user.Id, AccountActivityKind.PasskeyAdded);
            QueueSecurityMail(user, MfaOperation.AddPasskey);
            return Success(View(credential));
        }, ct);

    public Task<OperationResult<PasskeyPrimaryAuthentication, PasskeyFailure>> FinishLoginAsync(PasskeyBrowserCredential browser,
        string json, string origin, CancellationToken ct) => TransactionAsync(async () =>
        {
            var ceremony = await CeremonyAsync(browser, PasskeyCeremonyPurpose.Login, origin, ct);
            if (ceremony.FailureCode is { } failure) return Failure<PasskeyPrimaryAuthentication>(failure);
            var row = ceremony.Value!;
            var verified = await protocol.VerifyLoginAsync(State(row), json, origin, ct);
            Consume(row, verified.Succeeded);
            if (!verified.Succeeded) return Failure<PasskeyPrimaryAuthentication>(verified.FailureCode!.Value);
            var result = verified.Value!;
            var user = await db.Users.SingleOrDefaultAsync(value => value.Id == result.UserId && value.Kind == UserKind.Human
                && value.AccountStatus == UserAccountStatus.Active, ct);
            var credential = await db.UserPasskeys.SingleOrDefaultAsync(value => value.UserId == result.UserId
                && value.CredentialId == result.Credential.CredentialId && value.RelyingPartyId == Options.ServerDomain, ct);
            if (user is null || credential is null) return Failure<PasskeyPrimaryAuthentication>(PasskeyFailure.AccountUnavailable);
            credential.SignCount = result.Credential.SignCount; credential.IsBackedUp = result.Credential.IsBackedUp;
            credential.LastUsedAt = clock.GetUtcNow();
            return Success(new PasskeyPrimaryAuthentication(new(user.Id, user.UserName, user.Role, user.Kind, user.TokenVersion,
                user.EmailVerifiedAt is not null), credential.Id, row.ReturnPath));
        }, ct);

    public Task<OperationResult<PasskeyAccountCredential, PasskeyFailure>> RenameAsync(MfaActor actor, MfaBrowserCredential? proof,
        Guid id, string name, CancellationToken ct) => TransactionAsync(async () =>
        {
            if (!ValidName(name)) return Failure<PasskeyAccountCredential>(PasskeyFailure.InvalidName);
            var user = await UserAsync(actor, ct); if (user is null) return Failure<PasskeyAccountCredential>(PasskeyFailure.AccountUnavailable);
            var credential = await db.UserPasskeys.SingleOrDefaultAsync(value => value.Id == id && value.UserId == user.Id, ct);
            if (credential is null) return Failure<PasskeyAccountCredential>(PasskeyFailure.CredentialNotFound);
            var allowed = await AuthorizeChangeAsync(actor, proof, MfaOperation.RenamePasskey, id, ct);
            if (allowed is not null) return Failure<PasskeyAccountCredential>(allowed.Value);
            credential.Name = name.Trim(); return Success(View(credential));
        }, ct);

    public Task<OperationResult<bool, PasskeyFailure>> RemoveAsync(MfaActor actor, MfaBrowserCredential? proof, Guid id, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            var user = await UserAsync(actor, ct); if (user is null) return Failure<bool>(PasskeyFailure.AccountUnavailable);
            var credential = await db.UserPasskeys.SingleOrDefaultAsync(value => value.Id == id && value.UserId == user.Id, ct);
            if (credential is null) return Failure<bool>(PasskeyFailure.CredentialNotFound);
            var allowed = await AuthorizeChangeAsync(actor, proof, MfaOperation.RemovePasskey, id, ct);
            if (allowed is not null) return Failure<bool>(allowed.Value);
            db.UserPasskeys.Remove(credential); user.TokenVersion = checked(user.TokenVersion + 1);
            QueueSecurityMail(user, MfaOperation.RemovePasskey);
            Audit(user.Id, AccountActivityKind.PasskeyRemoved); return Success(true);
        }, ct);

    public Task CancelAsync(PasskeyBrowserCredential browser, CancellationToken ct) => TransactionAsync(async () =>
    {
        var row = await db.PasskeyCeremonies.SingleOrDefaultAsync(value => value.Id == browser.CeremonyId, ct);
        if (row is not null && row.BrowserBindingHash == crypto.HashBrowserSecret(browser.Secret))
        { row.State = PasskeyCeremonyState.Cancelled; row.ProtectedProtocolState = []; }
        return Success(true);
    }, ct);

    private async Task<PasskeyStartedCeremony> CreateCeremonyAsync(Guid? userId, int? version, PasskeyCeremonyPurpose purpose,
        PasskeyProtocolOptions options, string origin, string? name, string returnPath, CancellationToken ct, bool localProofSatisfied = false)
    {
        var secret = crypto.GenerateBrowserSecret(); var id = Guid.NewGuid(); var now = clock.GetUtcNow();
        var stamp = await db.PlatformSettings.AsNoTracking().IgnoreAutoIncludes().Where(value => value.Id == 1).Select(value => value.MfaPolicyStamp).SingleAsync(ct);
        var ceremony = new PasskeyCeremony { Id = id, UserId = userId, TokenVersion = version, MfaPolicyStamp = stamp, Purpose = purpose, LocalProofSatisfied = localProofSatisfied,
            BrowserBindingHash = crypto.HashBrowserSecret(secret), ConfigurationFingerprint = Options.Fingerprint, Origin = origin,
            ProtectedProtocolState = protector.Protect(options.State, PlatformSecretPurpose.PasskeyCeremonyState, userId ?? Guid.Empty, id),
            Name = name, CreatedAt = now, ExpiresAt = now.AddSeconds(Options.CeremonyLifetimeSeconds),
            ReturnPath = returnPath.StartsWith('/') && !returnPath.StartsWith("//", StringComparison.Ordinal) && !returnPath.Contains('\\') ? returnPath : "/" };
        db.PasskeyCeremonies.Add(ceremony);
        return new(options.OptionsJson, ceremony.ExpiresAt, new(id, secret));
    }
    private async Task<OperationResult<PasskeyCeremony, PasskeyFailure>> CeremonyAsync(PasskeyBrowserCredential browser,
        PasskeyCeremonyPurpose purpose, string origin, CancellationToken ct)
    {
        var row = await db.PasskeyCeremonies.SingleOrDefaultAsync(value => value.Id == browser.CeremonyId, ct);
        if (row is null || row.State != PasskeyCeremonyState.Pending || row.ExpiresAt <= clock.GetUtcNow()) return Failure<PasskeyCeremony>(PasskeyFailure.FlowExpired);
        if (row.BrowserBindingHash != crypto.HashBrowserSecret(browser.Secret)) return Failure<PasskeyCeremony>(PasskeyFailure.InvalidBrowser);
        if (row.Purpose != purpose || row.Origin != origin || !AvailableForOrigin(origin)) return Failure<PasskeyCeremony>(PasskeyFailure.InvalidOrigin);
        var stamp = await db.PlatformSettings.AsNoTracking().IgnoreAutoIncludes().Where(value => value.Id == 1).Select(value => value.MfaPolicyStamp).SingleAsync(ct);
        if (row.ConfigurationFingerprint != Options.Fingerprint || row.MfaPolicyStamp != stamp) return Failure<PasskeyCeremony>(PasskeyFailure.PolicyChanged);
        return Success(row);
    }
    private string State(PasskeyCeremony value) => protector.Unprotect(value.ProtectedProtocolState, PlatformSecretPurpose.PasskeyCeremonyState, value.UserId ?? Guid.Empty, value.Id);
    private static void Consume(PasskeyCeremony row, bool success) { row.State = success ? PasskeyCeremonyState.Completed : PasskeyCeremonyState.Failed; row.ProtectedProtocolState = []; }
    private Task<User?> UserAsync(MfaActor actor, CancellationToken ct) => db.Users.SingleOrDefaultAsync(value => value.Id == actor.UserId
        && value.Kind == UserKind.Human && value.AccountStatus == UserAccountStatus.Active && value.TokenVersion == actor.TokenVersion, ct);
    private bool RecentPrimary(MfaActor actor) => actor.Authentication.IsInteractive && actor.Authentication.AuthenticatedAt <= clock.GetUtcNow()
        && clock.GetUtcNow() - actor.Authentication.AuthenticatedAt <= TimeSpan.FromMinutes(5);
    private async Task<PasskeyFailure?> AuthorizeChangeAsync(MfaActor actor, MfaBrowserCredential? proof, MfaOperation operation, Guid target, CancellationToken ct)
    {
        if (!actor.Authentication.IsInteractive) return PasskeyFailure.PrimaryAuthenticationRequired;
        var account = await mfa.ReadAccountAsync(actor.UserId, ct);
        if (account is null || account.User.TokenVersion != actor.TokenVersion) return PasskeyFailure.AccountUnavailable;
        if (!account.Required) return RecentPrimary(actor) ? null : PasskeyFailure.PrimaryAuthenticationRequired;
        if (proof is null) return PasskeyFailure.StepUpRequired;
        return await proofs.ConsumeInTransactionAsync(actor, proof, operation, target, ct) is null ? null : PasskeyFailure.StepUpRequired;
    }
    private static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 64 && !name.Any(char.IsControl);
    private static PasskeyAccountCredential View(UserPasskey value) => new(value.Id, value.Name, value.CreatedAt, value.LastUsedAt, value.IsBackedUp, value.IsBackupEligible);
    private void Audit(Guid id, AccountActivityKind kind)
    {
        var notification = Privacy.AuthenticationActivity.Create(id, kind, source?.Address, clock.GetUtcNow());
        notification.UserId = id; notification.TargetType = NoCTF.Domain.Notifications.NotificationTargetType.User; notification.TargetId = id; db.Notifications.Add(notification);
    }
    private void QueueSecurityMail(User user, MfaOperation operation) => db.MfaChallenges.Add(new()
    {
        Id = Guid.NewGuid(), UserId = user.Id, Purpose = MfaChallengePurpose.SecurityNotification, State = MfaChallengeState.Completed,
        Operation = operation, TokenVersion = user.TokenVersion, CreatedAt = clock.GetUtcNow(), ExpiresAt = clock.GetUtcNow().AddMinutes(5), MailState = MfaMailState.Pending
    });
    private async Task<OperationResult<T, PasskeyFailure>> TransactionAsync<T>(Func<Task<OperationResult<T, PasskeyFailure>>> action, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            db.ChangeTracker.Clear();
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await action(); db.ChangeTracker.DetectChanges();
                var users = db.ChangeTracker.Entries<User>().Where(value => value.State == EntityState.Modified && value.Property(user => user.TokenVersion).IsModified).Select(value => value.Entity.Id).ToArray();
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
                if (publisher is not null && result.Succeeded)
                {
                    foreach (var user in users) { try { await publisher.PublishAsync(new MfaAuthenticationChanged(user)); } catch (Exception) when (!ct.IsCancellationRequested) { } }
                    foreach (var mail in db.ChangeTracker.Entries<MfaChallenge>().Where(value => value.Entity.MailState == MfaMailState.Pending))
                    { try { await publisher.PublishAsync(new SendMfaMail(mail.Entity.Id)); } catch (Exception) when (!ct.IsCancellationRequested) { } }
                }
                return result;
            }
            catch (Exception exception) when (attempt < 2 && (exception is DbUpdateConcurrencyException || TransactionFailureClassifier.IsRetryable(exception))) { db.ChangeTracker.Clear(); }
            catch (Exception exception) when (exception is DbException or DbUpdateException or CryptographicException or InvalidOperationException)
            { db.ChangeTracker.Clear(); return Failure<T>(PasskeyFailure.DependencyUnavailable); }
        }
        return Failure<T>(PasskeyFailure.DependencyUnavailable);
    }
    private static OperationResult<T, PasskeyFailure> Failure<T>(PasskeyFailure failure) => OperationResult<T, PasskeyFailure>.Failure(failure, "Passkey operation could not be completed.");
    private static OperationResult<T, PasskeyFailure> Success<T>(T value) => OperationResult<T, PasskeyFailure>.Success(value);
}
