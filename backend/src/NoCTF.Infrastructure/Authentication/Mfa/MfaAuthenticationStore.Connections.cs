using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Platform;

namespace NoCTF.Infrastructure.Authentication.Mfa;

public sealed partial class MfaAuthenticationStore
{
    private sealed record ContextUser(Guid Id, UserKind Kind, UserRole Role, UserAccountStatus AccountStatus,
        int TokenVersion, bool MfaRequired, Guid? CredentialId, MfaPolicy Policy, bool Privileged);

    private IQueryable<ContextUser> ContextUsers(Guid[] ids) => db.Users.AsNoTracking().IgnoreAutoIncludes().Where(user => ids.Contains(user.Id))
        .Select(user => new ContextUser(user.Id, user.Kind, user.Role, user.AccountStatus, user.TokenVersion, user.MfaRequired,
            db.UserTotpCredentials.Where(credential => credential.UserId == user.Id).Select(credential => (Guid?)credential.Id).FirstOrDefault(),
            db.PlatformSettings.Where(settings => settings.Id == 1).Select(settings => settings.MfaPolicy).First(),
            user.Role != UserRole.Administrator && user.Role != UserRole.Organizer
                && db.PlatformSettings.Any(settings => settings.Id == 1 && settings.MfaPolicy == MfaPolicy.RequirePrivileged)
                && db.Competitions.Any(competition => competition.DeletedAt == null
                    && (competition.OwnerId == user.Id || competition.Collaborators.Any(collaborator => collaborator.UserId == user.Id
                        && (collaborator.Role == CompetitionCollaboratorRole.Manager || collaborator.Role == CompetitionCollaboratorRole.Judge))))));

    public async Task<IReadOnlyDictionary<string, MfaFailure?>> ValidateContextsAsync(IReadOnlyList<MfaContextValidationRequest> requests, CancellationToken ct)
    {
        if (requests.Count == 0) return new Dictionary<string, MfaFailure?>();
        var ids = requests.Select(value => value.UserId).Distinct().ToArray();
        var users = await ContextUsers(ids).ToDictionaryAsync(value => value.Id, ct);
        var passkeyIds = requests.Where(value => value.Authentication is { IsWellFormed: true, Method: AuthenticationMethod.Passkey })
            .Select(value => value.Authentication!.PrimaryCredentialId!.Value).Distinct().ToArray();
        var passkeys = passkeyIds.Length == 0 ? new Dictionary<Guid, Guid>()
            : await db.UserPasskeys.AsNoTracking().Where(value => passkeyIds.Contains(value.Id)).Select(value => new { value.Id, value.UserId }).ToDictionaryAsync(value => value.Id, value => value.UserId, ct);
        var settings = await db.PlatformSettings.AsNoTracking().IgnoreAutoIncludes().Where(value => value.Id == 1)
            .Select(value => new { value.MfaPolicy, value.SsoEnabled }).SingleAsync(ct);
        var privilegeCandidates = settings.MfaPolicy == MfaPolicy.RequirePrivileged
            ? users.Values.Where(value => value.Kind == UserKind.Human && !value.MfaRequired && value.CredentialId is null
                && value.Role is not (UserRole.Administrator or UserRole.Organizer)).Select(value => value.Id).ToArray()
            : [];
        var privileged = privilegeCandidates.Length == 0 ? [] : (await db.Users.AsNoTracking()
            .Where(user => privilegeCandidates.Contains(user.Id) && db.Competitions.Any(competition => competition.DeletedAt == null
                && (competition.OwnerId == user.Id || competition.Collaborators.Any(member => member.UserId == user.Id
                    && (member.Role == CompetitionCollaboratorRole.Manager || member.Role == CompetitionCollaboratorRole.Judge)))))
            .Select(value => value.Id).ToArrayAsync(ct)).ToHashSet();
        var providerIds = requests.Where(value => value.Authentication is { IsWellFormed: true, MfaSource: MfaSource.Oidc })
            .Select(value => value.Authentication!.ProviderId!.Value).Distinct().ToArray();
        var providers = providerIds.Length == 0 ? [] : await db.Set<OidcSsoProviderConfiguration>().AsNoTracking().IgnoreAutoIncludes()
            .Where(value => providerIds.Contains(value.Id) && value.Enabled && value.AllowLogin && value.MfaTrustEnabled && value.MfaTrustPolicyId != Guid.Empty)
            .Select(value => new { value.Id, value.MfaTrustPolicyId }).ToDictionaryAsync(value => value.Id, value => value.MfaTrustPolicyId, ct);
        var result = new Dictionary<string, MfaFailure?>(); var now = clock.GetUtcNow();
        foreach (var request in requests)
        {
            MfaFailure? failure = null;
            if (!users.TryGetValue(request.UserId, out var user) || user.AccountStatus != UserAccountStatus.Active || user.TokenVersion != request.TokenVersion) failure = MfaFailure.AccountUnavailable;
            else if (user.Kind == UserKind.Human)
            {
                var context = request.Authentication;
                var credential = user.CredentialId;
                if (context is null || !context.IsWellFormed || context.Method == AuthenticationMethod.Bot || context.AuthenticatedAt > now.AddSeconds(30)) failure = MfaFailure.PrimaryAuthenticationRequired;
                else if (context.Method == AuthenticationMethod.Passkey && (!passkeys.TryGetValue(context.PrimaryCredentialId!.Value, out var owner) || owner != user.Id)) failure = MfaFailure.PrimaryAuthenticationRequired;
                else if (context.HasMfa && (context.MfaAuthenticatedAt > now.AddSeconds(30) || context.MfaDeadline <= now)) failure = MfaFailure.PrimaryAuthenticationRequired;
                else if (context.IsLocalMfa && context.CredentialId != credential) failure = MfaFailure.PrimaryAuthenticationRequired;
                else if (context.MfaSource == MfaSource.Oidc && (!settings.SsoEnabled || !providers.TryGetValue(context.ProviderId!.Value, out var policyId)
                    || policyId != context.TrustPolicyId)) failure = MfaFailure.PrimaryAuthenticationRequired;
                else if (!context.HasMfa && MfaRequirements.IsRequired(user.Kind, user.Role, user.MfaRequired, settings.MfaPolicy, privileged.Contains(user.Id), credential is not null))
                    failure = credential is null ? MfaFailure.NotEnrolled : MfaFailure.LocalMfaRequired;
            }
            result[request.Key] = failure;
        }
        return result;
    }
}
