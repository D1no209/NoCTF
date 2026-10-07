using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Platform;

namespace NoCTF.Infrastructure.Authentication.Mfa;

public sealed partial class MfaAuthenticationStore
{
    public async Task<IReadOnlyDictionary<string, MfaFailure?>> ValidateContextsAsync(IReadOnlyList<MfaContextValidationRequest> requests, CancellationToken ct)
    {
        if (requests.Count == 0) return new Dictionary<string, MfaFailure?>();
        var ids = requests.Select(value => value.UserId).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(value => ids.Contains(value.Id)).ToDictionaryAsync(value => value.Id, ct);
        var credentials = await db.UserTotpCredentials.AsNoTracking().Where(value => ids.Contains(value.UserId)).Select(value => new { value.UserId, value.Id }).ToDictionaryAsync(value => value.UserId, value => value.Id, ct);
        var passkeyIds = requests.Where(value => value.Authentication?.PrimaryCredentialId is not null).Select(value => value.Authentication!.PrimaryCredentialId!.Value).Distinct().ToArray();
        var passkeys = await db.UserPasskeys.AsNoTracking().Where(value => passkeyIds.Contains(value.Id)).Select(value => new { value.Id, value.UserId }).ToDictionaryAsync(value => value.Id, value => value.UserId, ct);
        var settings = await db.PlatformSettings.AsNoTracking().IgnoreAutoIncludes().SingleAsync(value => value.Id == 1, ct);
        var owners = await db.Competitions.AsNoTracking().Where(value => value.DeletedAt == null && ids.Contains(value.OwnerId)).Select(value => value.OwnerId).ToArrayAsync(ct);
        var collaborators = await db.Competitions.AsNoTracking().Where(value => value.DeletedAt == null).SelectMany(value => value.Collaborators)
            .Where(value => ids.Contains(value.UserId) && (value.Role == CompetitionCollaboratorRole.Manager || value.Role == CompetitionCollaboratorRole.Judge)).Select(value => value.UserId).ToArrayAsync(ct);
        var privileged = owners.Concat(collaborators).ToHashSet();
        var providerIds = requests.Where(value => value.Authentication?.MfaSource == MfaSource.Oidc).Select(value => value.Authentication!.ProviderId!.Value).Distinct().ToArray();
        var providers = await db.Set<OidcSsoProviderConfiguration>().AsNoTracking().IgnoreAutoIncludes().Where(value => providerIds.Contains(value.Id))
            .Select(value => new { value.Id, value.Enabled, value.AllowLogin, value.MfaTrustEnabled, value.MfaTrustPolicyId }).ToDictionaryAsync(value => value.Id, ct);
        var result = new Dictionary<string, MfaFailure?>(); var now = clock.GetUtcNow();
        foreach (var request in requests)
        {
            MfaFailure? failure = null;
            if (!users.TryGetValue(request.UserId, out var user) || user.AccountStatus != UserAccountStatus.Active || user.TokenVersion != request.TokenVersion) failure = MfaFailure.AccountUnavailable;
            else if (user.Kind == UserKind.Human)
            {
                var context = request.Authentication;
                var credential = credentials.GetValueOrDefault(user.Id);
                if (context is null || !context.IsWellFormed || context.Method == AuthenticationMethod.Bot || context.AuthenticatedAt > now.AddSeconds(30)) failure = MfaFailure.PrimaryAuthenticationRequired;
                else if (context.Method == AuthenticationMethod.Passkey && (!passkeys.TryGetValue(context.PrimaryCredentialId!.Value, out var owner) || owner != user.Id)) failure = MfaFailure.PrimaryAuthenticationRequired;
                else if (context.HasMfa && (context.MfaAuthenticatedAt > now.AddSeconds(30) || context.MfaDeadline <= now)) failure = MfaFailure.PrimaryAuthenticationRequired;
                else if (context.IsLocalMfa && context.CredentialId != credential) failure = MfaFailure.PrimaryAuthenticationRequired;
                else if (context.MfaSource == MfaSource.Oidc && (!settings.SsoEnabled || !providers.TryGetValue(context.ProviderId!.Value, out var provider)
                    || !provider.Enabled || !provider.AllowLogin || !provider.MfaTrustEnabled || provider.MfaTrustPolicyId != context.TrustPolicyId)) failure = MfaFailure.PrimaryAuthenticationRequired;
                else if (!context.HasMfa && MfaRequirements.IsRequired(user.Kind, user.Role, user.MfaRequired, settings.MfaPolicy, privileged.Contains(user.Id), credential != Guid.Empty))
                    failure = credential == Guid.Empty ? MfaFailure.NotEnrolled : MfaFailure.LocalMfaRequired;
            }
            result[request.Key] = failure;
        }
        return result;
    }
}
