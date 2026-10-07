using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;

namespace NoCTF.Infrastructure.Authentication.Mfa;

public sealed partial class MfaAuthenticationStore
{
    public async Task<OperationResult<MfaPlatformConfiguration, MfaFailure>> ReadConfigurationAsync(MfaActor actor, CancellationToken ct)
    {
        var account = await ReadAccountAsync(actor.UserId, ct);
        if (account is null || account.User.Kind != UserKind.Human || account.User.Role != UserRole.Administrator
            || account.User.TokenVersion != actor.TokenVersion) return Failure<MfaPlatformConfiguration>(MfaFailure.NotApplicable);
        var settings = await SettingsAsync(ct);
        var configured = await db.Set<NoCTF.Domain.Platform.OidcSsoProviderConfiguration>().AsNoTracking()
            .Include(value => value.MfaAcrEntries).Include(value => value.MfaAmrGroups).ThenInclude(value => value.Values).AsSplitQuery().ToArrayAsync(ct);
        var providers = configured
            .Select(value => new MfaProviderTrust(value.Id, value.Name,
                new OidcMfaTrust(value.MfaTrustEnabled, value.MfaTrustPolicyId, value.MfaAuthenticationMaxAgeSeconds,
                    value.MfaAcrEntries.OrderBy(member => member.Position).Select(member => member.Value).ToArray(),
                    value.MfaAmrGroups.OrderBy(group => group.Position).Select(group => (IReadOnlyList<string>)group.Values
                        .OrderBy(member => member.Position).Select(member => member.Value).ToArray()).ToArray()))).ToArray();
        return Success(new MfaPlatformConfiguration(settings.MfaPolicy, providers));
    }

    public async Task<OperationResult<MfaCreatedFlow, MfaFailure>> BeginOwnEnrollmentAsync(MfaActor actor, CancellationToken ct)
    {
        var account = await ReadAccountAsync(actor.UserId, ct);
        if (account is null || account.User.Kind != UserKind.Human || account.User.TokenVersion != actor.TokenVersion) return Failure<MfaCreatedFlow>(MfaFailure.NotApplicable);
        if (account.CredentialId is not null) return Failure<MfaCreatedFlow>(MfaFailure.AlreadyEnrolled);
        if (!actor.Authentication.IsInteractive || clock.GetUtcNow() - actor.Authentication.AuthenticatedAt > TimeSpan.FromMinutes(5)) return Failure<MfaCreatedFlow>(MfaFailure.PrimaryAuthenticationRequired);
        var (flow, browser) = await CreateFlowAsync(new(account.User, actor.Authentication.Method, actor.Authentication.AuthenticatedAt, ProviderId: actor.Authentication.ProviderId), MfaChallengePurpose.Enrollment, ct);
        var challenge = await db.MfaChallenges.SingleAsync(value => value.Id == flow.Id, ct);
        challenge.Operation = MfaOperation.EnableTotp;
        await db.SaveChangesAsync(ct);
        return Success(new MfaCreatedFlow(flow, browser));
    }

    public async Task<OperationResult<MfaCreatedFlow, MfaFailure>> BeginStepUpAsync(MfaActor actor, MfaOperation operation, Guid? target, CancellationToken ct)
    {
        var account = await ReadAccountAsync(actor.UserId, ct);
        if (account is null || account.User.Kind != UserKind.Human || account.User.TokenVersion != actor.TokenVersion || !actor.Authentication.IsInteractive) return Failure<MfaCreatedFlow>(MfaFailure.NotApplicable);
        if (account.CredentialId is null) return Failure<MfaCreatedFlow>(MfaFailure.LocalMfaRequired);
        if (IsAdministrative(operation) && account.User.Role != UserRole.Administrator) return Failure<MfaCreatedFlow>(MfaFailure.NotApplicable);
        var (flow, browser) = await CreateFlowAsync(new(account.User, actor.Authentication.Method, actor.Authentication.AuthenticatedAt, ProviderId: actor.Authentication.ProviderId), MfaChallengePurpose.StepUp, ct);
        var challenge = await db.MfaChallenges.SingleAsync(value => value.Id == flow.Id, ct);
        challenge.Operation = operation; challenge.TargetResourceId = target;
        await db.SaveChangesAsync(ct);
        return Success(new MfaCreatedFlow(flow, browser));
    }

    public Task<OperationResult<MfaFlowView, MfaFailure>> VerifyStepUpAsync(MfaBrowserCredential browser, MfaVerification verification, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            var flow = await FindFlowAsync(browser, ct);
            if (flow.Failure is { } error) return Failure<MfaFlowView>(error);
            var challenge = flow.Challenge!; var user = flow.User!;
            if (challenge.Purpose != MfaChallengePurpose.StepUp || challenge.State != MfaChallengeState.Pending) return Failure<MfaFlowView>(MfaFailure.StepUpRequired);
            var credential = await db.UserTotpCredentials.SingleOrDefaultAsync(value => value.UserId == user.Id && value.Id == challenge.CredentialId, ct);
            if (credential is null) return Failure<MfaFlowView>(MfaFailure.LocalMfaRequired);
            var failure = await VerifyFactorAsync(user, credential, verification, ct);
            if (failure is not null) return await FailedAsync<MfaFlowView>(challenge, user.Id, failure.Value, ct);
            challenge.State = MfaChallengeState.Verified;
            return Success(View(challenge, user.UserName, false));
        }, ct);

    private async Task<(User? User, MfaChallenge? Challenge, MfaFailure? Failure)> ProofAsync(MfaActor actor, MfaBrowserCredential proof, MfaOperation operation, Guid? target, CancellationToken ct)
    {
        var flow = await FindFlowAsync(proof, ct);
        if (flow.Failure is { } failure) return (null, null, failure);
        var challenge = flow.Challenge!; var user = flow.User!;
        if (user.Id != actor.UserId || user.TokenVersion != actor.TokenVersion || !actor.Authentication.IsInteractive
            || challenge.Purpose != MfaChallengePurpose.StepUp || challenge.State != MfaChallengeState.Verified
            || challenge.Operation != operation || challenge.TargetResourceId != target) return (null, null, MfaFailure.StepUpRequired);
        if (IsAdministrative(operation) && user.Role != UserRole.Administrator) return (null, null, MfaFailure.NotApplicable);
        return (user, challenge, null);
    }

    public Task<OperationResult<AuthenticatedUser, MfaFailure>> ConsumeStepUpAsync(MfaActor actor, MfaBrowserCredential proof, MfaOperation operation, Guid? target, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            var verified = await ProofAsync(actor, proof, operation, target, ct);
            if (verified.Failure is { } failure) return Failure<AuthenticatedUser>(failure);
            verified.Challenge!.State = MfaChallengeState.Completed;
            return Success(ToAuthenticated(verified.User!));
        }, ct);

    public Task<OperationResult<MfaCreatedFlow, MfaFailure>> BeginRebindAsync(MfaActor actor, MfaBrowserCredential proof, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            var verified = await ProofAsync(actor, proof, MfaOperation.RebindTotp, actor.UserId, ct);
            if (verified.Failure is { } failure) return Failure<MfaCreatedFlow>(failure);
            verified.Challenge!.State = MfaChallengeState.Completed;
            var (flow, browser) = await CreateFlowAsync(new(ToAuthenticated(verified.User!), actor.Authentication.Method, actor.Authentication.AuthenticatedAt, ProviderId: actor.Authentication.ProviderId), MfaChallengePurpose.Rebind, ct);
            var challenge = await db.MfaChallenges.SingleAsync(value => value.Id == flow.Id, ct);
            challenge.Operation = MfaOperation.RebindTotp;
            return Success(new MfaCreatedFlow(flow, browser));
        }, ct);

    public Task<OperationResult<IReadOnlyList<string>, MfaFailure>> RegenerateRecoveryCodesAsync(MfaActor actor, MfaBrowserCredential proof, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            var verified = await ProofAsync(actor, proof, MfaOperation.RegenerateRecoveryCodes, actor.UserId, ct);
            if (verified.Failure is { } failure) return Failure<IReadOnlyList<string>>(failure);
            var credential = await db.UserTotpCredentials.SingleAsync(value => value.UserId == actor.UserId, ct);
            credential.RecoveryBatchId = Guid.NewGuid();
            var codes = NewRecoveryCodes(actor.UserId, credential.RecoveryBatchId);
            verified.User!.TokenVersion = checked(verified.User.TokenVersion + 1);
            verified.Challenge!.State = MfaChallengeState.Completed; verified.Challenge.MailState = MfaMailState.Pending;
            SecurityEvent(actor.UserId, NoCTF.Application.Authentication.Privacy.AccountActivityKind.MfaRecoveryCodesRegenerated);
            return Success(codes);
        }, ct);

    public Task<OperationResult<MfaChangeResult, MfaFailure>> DisableAsync(MfaActor actor, MfaBrowserCredential proof, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            var verified = await ProofAsync(actor, proof, MfaOperation.DisableTotp, actor.UserId, ct);
            if (verified.Failure is { } failure) return Failure<MfaChangeResult>(failure);
            var user = verified.User!; var settings = await SettingsAsync(ct);
            if (MfaRequirements.IsMandated(user.Kind, user.Role, user.MfaRequired, settings.MfaPolicy, await CompetitionPrivilegedAsync(user.Id, ct))) return Failure<MfaChangeResult>(MfaFailure.PolicyRequiresMfa);
            var credential = await db.UserTotpCredentials.SingleAsync(value => value.UserId == user.Id, ct);
            db.UserTotpCredentials.Remove(credential);
            db.UserMfaRecoveryCodes.RemoveRange(await db.UserMfaRecoveryCodes.Where(value => value.UserId == user.Id).ToArrayAsync(ct));
            user.TokenVersion = checked(user.TokenVersion + 1);
            verified.Challenge!.State = MfaChallengeState.Completed; verified.Challenge.MailState = MfaMailState.Pending;
            SecurityEvent(actor.UserId, NoCTF.Application.Authentication.Privacy.AccountActivityKind.MfaDisabled);
            return Success(new MfaChangeResult(user.Id, user.TokenVersion));
        }, ct);

    public Task<OperationResult<MfaPolicy, MfaFailure>> ChangePolicyAsync(MfaActor actor, MfaBrowserCredential proof, MfaPolicy policy, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            var verified = await ProofAsync(actor, proof, MfaOperation.ChangePolicy, null, ct);
            if (verified.Failure is { } failure) return Failure<MfaPolicy>(failure);
            var settings = await SettingsAsync(ct);
            if (!Enum.IsDefined(policy)) return Failure<MfaPolicy>(MfaFailure.NotApplicable);
            if (policy != MfaPolicy.Optional && (verified.User!.EmailVerifiedAt is null || !MailConfigured(settings))) return Failure<MfaPolicy>(MfaFailure.EmailNotConfigured);
            if (settings.MfaPolicy != policy) { settings.MfaPolicy = policy; settings.MfaPolicyStamp = Guid.NewGuid(); }
            verified.Challenge!.State = MfaChallengeState.Completed;
            verified.Challenge.MailState = MfaMailState.Pending;
            SecurityEvent(actor.UserId, NoCTF.Application.Authentication.Privacy.AccountActivityKind.MfaPolicyChanged);
            return Success(policy);
        }, ct);

    public Task<OperationResult<MfaChangeResult, MfaFailure>> ChangeRequirementAsync(MfaActor actor, MfaBrowserCredential proof, Guid userId, bool required, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            var verified = await ProofAsync(actor, proof, MfaOperation.ChangeAccountRequirement, userId, ct);
            if (verified.Failure is { } failure) return Failure<MfaChangeResult>(failure);
            var user = await db.Users.SingleOrDefaultAsync(value => value.Id == userId, ct);
            if (user is null || user.Kind != UserKind.Human) return Failure<MfaChangeResult>(MfaFailure.NotApplicable);
            user.MfaRequired = required; user.TokenVersion = checked(user.TokenVersion + 1);
            verified.Challenge!.State = MfaChallengeState.Completed;
            verified.Challenge.MailState = MfaMailState.Pending;
            SecurityEvent(user.Id, NoCTF.Application.Authentication.Privacy.AccountActivityKind.MfaRequirementChanged, actor.UserId);
            return Success(new MfaChangeResult(user.Id, user.TokenVersion));
        }, ct);

    public Task<OperationResult<MfaRecoveryGrant, MfaFailure>> GrantRecoveryAsync(MfaActor? actor, MfaBrowserCredential? proof, Guid userId, string reason, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            if (actor is not null)
            {
                if (proof is null) return Failure<MfaRecoveryGrant>(MfaFailure.StepUpRequired);
                var verified = await ProofAsync(actor, proof, MfaOperation.GrantRecovery, userId, ct);
                if (verified.Failure is { } failure) return Failure<MfaRecoveryGrant>(failure);
                verified.Challenge!.State = MfaChallengeState.Completed;
            }
            var user = await db.Users.SingleOrDefaultAsync(value => value.Id == userId, ct);
            if (user is null || user.Kind != UserKind.Human || user.AccountStatus != UserAccountStatus.Active
                || actor is null && user.Role != UserRole.Administrator || string.IsNullOrWhiteSpace(reason) || reason.Length > 1024) return Failure<MfaRecoveryGrant>(MfaFailure.NotApplicable);
            if (user.EmailVerifiedAt is null) return Failure<MfaRecoveryGrant>(MfaFailure.EmailUnverified);
            var settings = await SettingsAsync(ct); if (!MailConfigured(settings)) return Failure<MfaRecoveryGrant>(MfaFailure.EmailNotConfigured);
            user.TokenVersion = checked(user.TokenVersion + 1);
            var token = crypto.GenerateBrowserSecret(); var id = Guid.NewGuid(); var now = clock.GetUtcNow();
            var grant = new MfaChallenge { Id = id, UserId = user.Id, Purpose = MfaChallengePurpose.RecoveryGrant, CreatedAt = now, ExpiresAt = now.AddMinutes(30),
                TokenVersion = user.TokenVersion, PolicyStamp = settings.MfaPolicyStamp, ActorUserId = actor?.UserId, Reason = reason.Trim(), MailState = MfaMailState.Pending,
                RecoveryGrantSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(token)), RecoveryGrantCiphertext = protector.Protect(token, PlatformSecretPurpose.MfaRecoveryGrant, user.Id, id) };
            db.MfaChallenges.Add(grant); SecurityEvent(user.Id, NoCTF.Application.Authentication.Privacy.AccountActivityKind.MfaRecoveryGranted, actor?.UserId);
            return Success(new MfaRecoveryGrant(id, user.Id, grant.ExpiresAt));
        }, ct);

    public Task<OperationResult<MfaCreatedFlow, MfaFailure>> ExchangeRecoveryAsync(string token, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            if (token.Length != 43) return Failure<MfaCreatedFlow>(MfaFailure.InvalidRecoveryGrant);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var grant = await db.MfaChallenges.SingleOrDefaultAsync(value => value.Purpose == MfaChallengePurpose.RecoveryGrant && value.RecoveryGrantSha256 == hash, ct);
            if (grant is null || grant.State != MfaChallengeState.Pending || grant.ExpiresAt <= clock.GetUtcNow()) return Failure<MfaCreatedFlow>(MfaFailure.InvalidRecoveryGrant);
            var user = await db.Users.SingleOrDefaultAsync(value => value.Id == grant.UserId && value.AccountStatus == UserAccountStatus.Active && value.Kind == UserKind.Human && value.TokenVersion == grant.TokenVersion && value.EmailVerifiedAt != null, ct);
            if (user is null || grant.PolicyStamp != (await SettingsAsync(ct)).MfaPolicyStamp) return Failure<MfaCreatedFlow>(MfaFailure.InvalidRecoveryGrant);
            grant.State = MfaChallengeState.Completed; grant.RecoveryGrantCiphertext = null; grant.MailState = MfaMailState.Sent;
            var secret = crypto.GenerateBrowserSecret(); var now = clock.GetUtcNow();
            var challenge = new MfaChallenge { Id = Guid.NewGuid(), UserId = user.Id, Purpose = MfaChallengePurpose.RecoveryEnrollment, CreatedAt = now,
                ExpiresAt = now.AddMinutes(10) < grant.ExpiresAt ? now.AddMinutes(10) : grant.ExpiresAt, BrowserBindingHash = crypto.HashBrowserSecret(secret),
                TokenVersion = user.TokenVersion, PolicyStamp = grant.PolicyStamp };
            db.MfaChallenges.Add(challenge);
            return Success(new MfaCreatedFlow(View(challenge, user.UserName, false), new(challenge.Id, secret)));
        }, ct);

    private static bool IsAdministrative(MfaOperation operation) => operation is MfaOperation.ChangePolicy or MfaOperation.ChangeOidcTrust
        or MfaOperation.ChangeAccountRequirement or MfaOperation.GrantRecovery;

    public Task<OperationResult<OidcMfaTrust, MfaFailure>> ChangeOidcTrustAsync(MfaActor actor, MfaBrowserCredential proof, Guid providerId,
        bool enabled, int maxAgeSeconds, IReadOnlyList<string> acr, IReadOnlyList<IReadOnlyList<string>> amr, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            var verified = await ProofAsync(actor, proof, MfaOperation.ChangeOidcTrust, providerId, ct);
            if (verified.Failure is { } failure) return Failure<OidcMfaTrust>(failure);
            if (maxAgeSeconds is < 1 or > 300 || enabled && acr.Count == 0 && amr.Count == 0
                || acr.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 512)
                || amr.Any(group => group.Count == 0 || group.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 128))) return Failure<OidcMfaTrust>(MfaFailure.NotApplicable);
            var provider = await db.Set<NoCTF.Domain.Platform.OidcSsoProviderConfiguration>().Include(value => value.MfaAcrEntries)
                .Include(value => value.MfaAmrGroups).ThenInclude(value => value.Values).SingleOrDefaultAsync(value => value.Id == providerId, ct);
            if (provider is null) return Failure<OidcMfaTrust>(MfaFailure.NotApplicable);
            db.RemoveRange(provider.MfaAcrEntries); db.RemoveRange(provider.MfaAmrGroups); await db.SaveChangesAsync(ct);
            provider.MfaTrustEnabled = enabled; provider.MfaTrustPolicyId = Guid.NewGuid(); provider.MfaAuthenticationMaxAgeSeconds = maxAgeSeconds;
            provider.MfaAcrEntries = acr.Distinct(StringComparer.Ordinal).Select((value, index) => new NoCTF.Domain.Platform.OidcMfaAcr { SsoProviderId = provider.Id, Position = index, Value = value }).ToList();
            provider.MfaAmrGroups = amr.Select((group, index) =>
            {
                var id = Guid.NewGuid(); return new NoCTF.Domain.Platform.OidcMfaAmrGroup { Id = id, SsoProviderId = provider.Id, Position = index,
                    Values = group.Distinct(StringComparer.Ordinal).Select((value, position) => new NoCTF.Domain.Platform.OidcMfaAmrValue { GroupId = id, Position = position, Value = value }).ToList() };
            }).ToList();
            verified.Challenge!.State = MfaChallengeState.Completed;
            verified.Challenge.MailState = MfaMailState.Pending;
            SecurityEvent(actor.UserId, NoCTF.Application.Authentication.Privacy.AccountActivityKind.OidcMfaTrustChanged);
            return Success(new OidcMfaTrust(enabled, provider.MfaTrustPolicyId, maxAgeSeconds, acr, amr));
        }, ct);

    public Task<OperationResult<MfaFlowView, MfaFailure>> ApplyRecoveryPrimaryAsync(MfaBrowserCredential browser, PrimaryAuthentication primary, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            var flow = await FindFlowAsync(browser, ct);
            if (flow.Failure is { } failure) return Failure<MfaFlowView>(failure);
            if (flow.Challenge!.Purpose != MfaChallengePurpose.RecoveryEnrollment) return Failure<MfaFlowView>(MfaFailure.NotApplicable);
            if (flow.User!.Id != primary.User.Id || flow.User.TokenVersion != primary.User.TokenVersion
                || primary.Method is not (AuthenticationMethod.Password or AuthenticationMethod.Oidc or AuthenticationMethod.Cas)) return Failure<MfaFlowView>(MfaFailure.InvalidRecoveryGrant);
            flow.Challenge.PrimaryMethod = primary.Method; flow.Challenge.PrimaryAuthenticatedAt = primary.AuthenticatedAt;
            flow.Challenge.PrimaryProviderId = primary.ProviderId;
            flow.Challenge.PrimaryProviderFingerprint = primary.ProviderId is { } providerId ? await ProviderFingerprintAsync(providerId, ct) : null;
            return Success(View(flow.Challenge, flow.User.UserName, false));
        }, ct);
}
