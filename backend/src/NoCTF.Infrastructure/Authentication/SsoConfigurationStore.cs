using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class SsoConfigurationStore(
    NoCtfDbContext db,
    PlatformSecretProtector secrets,
    SsoNetworkOptions networkOptions) : ISsoConfigurationStore
{
    private const short SettingsId = 1;
    private static readonly AsyncKeyedLock.AsyncKeyedLocker<string> LocalMutationLocks = new();
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SsoConfigurationView> GetAsync(CancellationToken ct)
    {
        var settings = await db.PlatformSettings.AsNoTracking()
            .AsSplitQuery()
            .SingleAsync(item => item.Id == SettingsId, ct);
        return ToView(settings.SsoConfiguration, settings.UpdatedAt);
    }

    public Task<SsoConfigurationMutationResult> UpdateGlobalAsync(
        bool enabled,
        string publicBaseUrl,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!SsoProviderValidation.IsValidPublicBaseUrl(
                publicBaseUrl,
                networkOptions.AllowInsecurePublicBaseUrl))
            return Task.FromResult(new SsoConfigurationMutationResult(
                SsoConfigurationMutationState.InvalidConfiguration));

        return MutateAsync(actorUserId, now, ct, configuration =>
        {
            if (enabled && configuration.Providers.All(provider =>
                    !provider.Enabled || !provider.AllowLogin && !provider.AllowBinding))
                return Mutation.Invalid(SsoConfigurationMutationState.InvalidConfiguration);
            configuration.Enabled = enabled;
            configuration.PublicBaseUrl = publicBaseUrl;
            return Mutation.Success(
                new SsoProviderAuditFact(
                    1,
                    SsoProviderAuditAction.GlobalConfigurationUpdated,
                    null,
                    null,
                    null));
        });
    }

    public Task<SsoConfigurationMutationResult> CreateProviderAsync(
        Guid providerId,
        SsoProviderDraft provider,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!SsoProviderValidation.IsValid(provider, networkOptions.AllowInsecureProviderUrls))
            return Task.FromResult(new SsoConfigurationMutationResult(
                SsoConfigurationMutationState.InvalidConfiguration));

        return MutateAsync(actorUserId, now, ct, configuration =>
        {
            if (configuration.Providers.Count >= SsoRules.MaximumProviders)
                return Mutation.Invalid(SsoConfigurationMutationState.ProviderLimitReached);
            if (configuration.Providers.Any(item => SameTrustBoundary(item, provider)))
                return Mutation.Invalid(SsoConfigurationMutationState.DuplicateTrustBoundary);
            if (provider.Protocol == SsoProtocol.Oidc
                && provider.Enabled
                && (provider.AllowLogin || provider.AllowBinding))
                return Mutation.Invalid(SsoConfigurationMutationState.SecretRequired);

            var entity = ToConfiguration(providerId, provider, secret: null);
            configuration.Providers.Add(entity);
            db.Add(entity);
            return Mutation.Success(
                new SsoProviderAuditFact(
                    1,
                    SsoProviderAuditAction.ProviderCreated,
                    providerId,
                    provider.Name,
                    provider.Protocol),
                providerId);
        });
    }

    public Task<SsoConfigurationMutationResult> UpdateProviderAsync(
        Guid providerId,
        SsoProviderDraft provider,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!SsoProviderValidation.IsValid(provider, networkOptions.AllowInsecureProviderUrls))
            return Task.FromResult(new SsoConfigurationMutationResult(
                SsoConfigurationMutationState.InvalidConfiguration));

        return MutateAsync(actorUserId, now, ct, async configuration =>
        {
            var index = configuration.Providers.FindIndex(item => item.Id == providerId);
            if (index < 0)
                return Mutation.Invalid(SsoConfigurationMutationState.ProviderNotFound);
            var current = configuration.Providers[index];
            var hasBindings = await db.Users.AsNoTracking().AnyAsync(
                user => user.ExternalIdentity != null
                    && user.ExternalIdentity.ProviderId == providerId,
                ct);
            if (hasBindings && !SameTrustBoundary(current, provider))
                return Mutation.Invalid(SsoConfigurationMutationState.TrustBoundaryImmutable);
            if (configuration.Providers.Where(item => item.Id != providerId)
                .Any(item => SameTrustBoundary(item, provider)))
                return Mutation.Invalid(SsoConfigurationMutationState.DuplicateTrustBoundary);
            var secret = current.Oidc?.ClientSecretCiphertext;
            if (provider.Protocol == SsoProtocol.Oidc
                && provider.Enabled
                && (provider.AllowLogin || provider.AllowBinding)
                && secret is null)
                return Mutation.Invalid(SsoConfigurationMutationState.SecretRequired);

            configuration.Providers[index] = ToConfiguration(providerId, provider, secret);
            return Mutation.Success(new SsoProviderAuditFact(
                1,
                SsoProviderAuditAction.ProviderUpdated,
                providerId,
                provider.Name,
                provider.Protocol), providerId);
        });
    }

    public Task<SsoConfigurationMutationResult> ReplaceSecretAsync(
        Guid providerId,
        string secret,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(secret)
            || secret.Length > SsoRules.MaximumSecretLength
            || secret.Any(char.IsControl))
            return Task.FromResult(new SsoConfigurationMutationResult(
                SsoConfigurationMutationState.InvalidConfiguration));

        return MutateAsync(actorUserId, now, ct, configuration =>
        {
            var provider = configuration.Providers.SingleOrDefault(item => item.Id == providerId);
            if (provider is null)
                return Mutation.Invalid(SsoConfigurationMutationState.ProviderNotFound);
            if (provider.Protocol != SsoProtocol.Oidc || provider.Oidc is null)
                return Mutation.Invalid(SsoConfigurationMutationState.InvalidConfiguration);
            provider.Oidc.ClientSecretCiphertext = secrets.Protect(
                secret,
                PlatformSecretPurpose.SsoOidcClientSecret,
                providerId);
            return Mutation.Success(new SsoProviderAuditFact(
                1,
                SsoProviderAuditAction.ProviderSecretReplaced,
                providerId,
                provider.Name,
                provider.Protocol), providerId);
        });
    }

    private async Task<SsoConfigurationMutationResult> MutateAsync(
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct,
        Func<SsoConfiguration, Mutation> mutate) =>
        await MutateAsync(actorUserId, now, ct,
            configuration => Task.FromResult(mutate(configuration)));

    private async Task<SsoConfigurationMutationResult> MutateAsync(
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct,
        Func<SsoConfiguration, Task<Mutation>> mutate)
    {
        using var localLease = await LocalMutationLocks.LockAsync("platform-sso", ct);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;
            try
            {
                var settings = await db.PlatformSettings.AsSplitQuery().SingleAsync(
                    item => item.Id == SettingsId,
                    ct);
                var configuration = settings.SsoConfiguration;
                var persistedProviderIds = configuration.Providers
                    .Select(provider => provider.Id)
                    .ToHashSet();
                var mutation = await mutate(configuration);
                if (mutation.State != SsoConfigurationMutationState.Updated)
                    return new(mutation.State);

                settings.SsoConfiguration = configuration;
                foreach (var provider in configuration.Providers.Where(provider =>
                             !persistedProviderIds.Contains(provider.Id)))
                {
                    db.Add(provider);
                    foreach (var host in provider.AllowedHostEntries)
                        db.Entry(host).State = EntityState.Added;
                    if (provider is OidcSsoProviderConfiguration oidc)
                    {
                        foreach (var scope in oidc.ScopeEntries)
                            db.Entry(scope).State = EntityState.Added;
                    }
                }
                settings.UpdatedAt = now;
                db.Notifications.Add(ToAuditNotification(actorUserId, mutation.Audit!, now));
                await db.SaveChangesAsync(ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);
                return new(
                    SsoConfigurationMutationState.Updated,
                    ToView(configuration, now),
                    mutation.ProviderId);
            }
            catch (Exception exception) when (exception is DbUpdateConcurrencyException
                || TransactionFailureClassifier.IsRetryable(exception))
            {
                if (attempt == 2)
                {
                    if (exception is DbUpdateConcurrencyException concurrency)
                    {
                        throw new DbUpdateConcurrencyException(
                            $"SSO configuration concurrency persisted for: {string.Join(", ", concurrency.Entries.Select(entry => entry.Metadata.DisplayName()))}",
                            concurrency);
                    }
                    throw;
                }
                db.ChangeTracker.Clear();
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Random.Shared.Next(15, 51)),
                    ct);
            }
        }
        throw new InvalidOperationException("SSO configuration retry budget was exhausted.");
    }

    private static SsoConfiguration Clone(SsoConfiguration configuration) => new()
    {
        Enabled = configuration.Enabled,
        PublicBaseUrl = configuration.PublicBaseUrl,
        Providers = configuration.Providers.Select(CloneProvider).ToList()
    };

    private static SsoProviderConfiguration CloneProvider(
        SsoProviderConfiguration provider) => provider switch
    {
        OidcSsoProviderConfiguration oidc => Common(new OidcSsoProviderConfiguration
        {
            Issuer = oidc.Issuer,
            DiscoveryUrl = oidc.DiscoveryUrl,
            ClientId = oidc.ClientId,
            ClientSecretCiphertext = oidc.ClientSecretCiphertext?.ToArray(),
            Scopes = oidc.Scopes.ToArray(),
            ReadUserInfo = oidc.ReadUserInfo,
            DisplayNameClaim = oidc.DisplayNameClaim
        }, provider),
        CasSsoProviderConfiguration cas => Common(new CasSsoProviderConfiguration
        {
            IdentityNamespace = cas.IdentityNamespace,
            LoginUrl = cas.LoginUrl,
            ServiceValidateUrl = cas.ServiceValidateUrl,
            DisplayNameAttribute = cas.DisplayNameAttribute
        }, provider),
        _ => throw new InvalidOperationException(
            $"Unsupported SSO provider type {provider.GetType().Name}.")
    };

    private static T Common<T>(T target, SsoProviderConfiguration source)
        where T : SsoProviderConfiguration
    {
        target.Id = source.Id;
        target.PlatformSettingsId = source.PlatformSettingsId;
        target.Name = source.Name;
        target.IconUrl = source.IconUrl;
        target.Enabled = source.Enabled;
        target.AllowLogin = source.AllowLogin;
        target.AllowBinding = source.AllowBinding;
        target.TimeoutSeconds = source.TimeoutSeconds;
        target.AllowedHosts = source.AllowedHosts.ToArray();
        return target;
    }

    private static SsoProviderConfiguration ToConfiguration(
        Guid id,
        SsoProviderDraft provider,
        byte[]? secret) => provider.Protocol switch
    {
        SsoProtocol.Oidc when provider.Oidc is not null => Common(
            new OidcSsoProviderConfiguration
            {
                Issuer = provider.Oidc.Issuer,
                DiscoveryUrl = provider.Oidc.DiscoveryUrl,
                ClientId = provider.Oidc.ClientId,
                ClientSecretCiphertext = secret,
                Scopes = provider.Oidc.Scopes.ToArray(),
                ReadUserInfo = provider.Oidc.ReadUserInfo,
                DisplayNameClaim = provider.Oidc.DisplayNameClaim
            }, id, provider),
        SsoProtocol.Cas when provider.Cas is not null => Common(
            new CasSsoProviderConfiguration
            {
                IdentityNamespace = provider.Cas.IdentityNamespace,
                LoginUrl = provider.Cas.LoginUrl,
                ServiceValidateUrl = provider.Cas.ServiceValidateUrl,
                DisplayNameAttribute = provider.Cas.DisplayNameAttribute
            }, id, provider),
        _ => throw new InvalidOperationException("The SSO provider draft is incomplete.")
    };

    private static T Common<T>(T target, Guid id, SsoProviderDraft source)
        where T : SsoProviderConfiguration
    {
        target.Id = id;
        target.Name = source.Name;
        target.IconUrl = source.IconUrl;
        target.Enabled = source.Enabled;
        target.AllowLogin = source.AllowLogin;
        target.AllowBinding = source.AllowBinding;
        target.TimeoutSeconds = source.TimeoutSeconds;
        target.AllowedHosts = source.AllowedHosts.ToArray();
        return target;
    }

    private static bool SameTrustBoundary(
        SsoProviderConfiguration current,
        SsoProviderDraft updated) =>
        current.Protocol == updated.Protocol
        && current.Protocol switch
        {
            SsoProtocol.Oidc => current.Oidc is not null && updated.Oidc is not null
                && string.Equals(current.Oidc.Issuer, updated.Oidc.Issuer, StringComparison.Ordinal)
                && string.Equals(current.Oidc.DiscoveryUrl, updated.Oidc.DiscoveryUrl, StringComparison.Ordinal)
                && string.Equals(current.Oidc.ClientId, updated.Oidc.ClientId, StringComparison.Ordinal),
            SsoProtocol.Cas => current.Cas is not null && updated.Cas is not null
                && string.Equals(current.Cas.IdentityNamespace, updated.Cas.IdentityNamespace, StringComparison.Ordinal)
                && string.Equals(current.Cas.LoginUrl, updated.Cas.LoginUrl, StringComparison.Ordinal)
                && string.Equals(current.Cas.ServiceValidateUrl, updated.Cas.ServiceValidateUrl, StringComparison.Ordinal),
            _ => false
        };

    private static SsoConfigurationView ToView(
        SsoConfiguration configuration,
        DateTimeOffset updatedAt) => new(
        configuration.Enabled,
        configuration.PublicBaseUrl,
        configuration.Providers.Select(provider => new SsoProviderView(
            provider.Id,
            provider.Name,
            provider.IconUrl,
            provider.Protocol,
            provider.Enabled,
            provider.AllowLogin,
            provider.AllowBinding,
            provider.TimeoutSeconds,
            provider.AllowedHosts,
            provider.Oidc is null ? null : new OidcSsoProviderView(
                provider.Oidc.Issuer,
                provider.Oidc.DiscoveryUrl,
                provider.Oidc.ClientId,
                provider.Oidc.Scopes,
                provider.Oidc.ReadUserInfo,
                provider.Oidc.DisplayNameClaim,
                provider.Oidc.ClientSecretCiphertext is { Length: > 0 }),
            provider.Cas is null ? null : new CasSsoProviderView(
                provider.Cas.IdentityNamespace,
                provider.Cas.LoginUrl,
                provider.Cas.ServiceValidateUrl,
                provider.Cas.DisplayNameAttribute))).ToArray(),
        updatedAt);

    private static Notification ToAuditNotification(
        Guid actorUserId,
        SsoProviderAuditFact fact,
        DateTimeOffset now)
    {
        return new SsoProviderConfigurationChangedNotification
        {
            Id = Guid.CreateVersion7(now),
            SourceType = NotificationSourceType.User,
            SourceId = actorUserId,
            TargetType = NotificationTargetType.PlatformAdministrators,
            TargetId = Notification.PlatformAdministratorsTargetId,
            ActionValue = (int)fact.Action,
            SsoProviderId = fact.ProviderId,
            ProviderName = fact.ProviderName,
            SsoProtocol = fact.Protocol,
            SentAt = now
        };
    }

    private sealed record Mutation(
        SsoConfigurationMutationState State,
        SsoProviderAuditFact? Audit = null,
        Guid? ProviderId = null)
    {
        public static Mutation Invalid(SsoConfigurationMutationState state) => new(state);
        public static Mutation Success(SsoProviderAuditFact audit, Guid? providerId = null) =>
            new(SsoConfigurationMutationState.Updated, audit, providerId);
    }
}

public sealed record SsoNetworkOptions(
    bool AllowInsecurePublicBaseUrl,
    bool AllowInsecureProviderUrls,
    IReadOnlySet<string> PrivateNetworkAllowList,
    IReadOnlySet<string> InsecureHttpHostAllowList);
