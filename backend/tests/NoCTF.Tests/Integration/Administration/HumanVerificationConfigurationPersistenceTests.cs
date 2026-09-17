using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Admission;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Caching;
using System.Text.Json;
using ZiggyCreatures.Caching.Fusion;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Administration;

[Category("Integration")]
public sealed class HumanVerificationConfigurationPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Provider_secrets_are_encrypted_write_only_and_used_by_runtime(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_human_verification")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var protector = new PlatformSecretProtector(Options.Create(
                new EmailVerificationProtectionOptions
                {
                    EncryptionKey = Convert.ToBase64String(
                        Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"))
                }));

            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var cacheServices = new ServiceCollection();
            cacheServices.AddFusionCache(NoCtfCacheNames.ReadModels);
            await using var cacheProviderServices =
                cacheServices.BuildServiceProvider();
            var cacheProvider = cacheProviderServices
                .GetRequiredService<IFusionCacheProvider>();
            var store = new HumanVerificationConfigurationStore(
                db,
                protector,
                Options.Create(new HumanVerificationOptions
                {
                    Provider = HumanVerificationProvider.Cap,
                    Cap = new()
                    {
                        ServerUrl = "https://deployment-cap.example.test",
                        BackendServerUrl = "http://noctf-cap:3000",
                        SiteKey = "deployment-site-key",
                        Secret = "deployment-secret"
                    }
                }),
                cacheProvider);

            var fallback = await store.GetRuntimeConfigurationAsync(cancellationToken);
            await Assert.That(fallback.Enabled).IsTrue();
            await Assert.That(fallback.RuntimeEnabled).IsTrue();
            await Assert.That(fallback.Options.Cap.Secret)
                .IsEqualTo("deployment-secret");

            await store.ReplaceSecretAsync(
                HumanVerificationProvider.Cap,
                "cap-secret",
                DateTimeOffset.UtcNow,
                cancellationToken);
            await store.ReplaceSecretAsync(
                HumanVerificationProvider.Turnstile,
                "turnstile-secret",
                DateTimeOffset.UtcNow,
                cancellationToken);
            var view = await store.UpdateAsync(new(
                true,
                HumanVerificationProvider.Cap,
                "https://cap.example.test/root",
                "site-key",
                string.Empty,
                [],
                DateTimeOffset.UtcNow,
                RuntimeEnabled: false,
                EvaluationEnabled: false), cancellationToken);

            await Assert.That(view.Enabled).IsTrue();
            await Assert.That(view.RuntimeEnabled).IsFalse();
            await Assert.That(view.EvaluationEnabled).IsFalse();
            await Assert.That(view.CapSecretConfigured).IsTrue();
            await Assert.That(view.TurnstileSecretConfigured).IsTrue();
            await Assert.That(typeof(HumanVerificationConfigurationView)
                .GetProperty("CapSecret")).IsNull();
            await Assert.That(typeof(HumanVerificationConfigurationView)
                .GetProperty("TurnstileSecret")).IsNull();
            var persisted = await db.PlatformSettings.AsNoTracking()
                .SingleAsync(cancellationToken);
            await Assert.That(persisted.HumanVerificationRuntimeEnabled).IsFalse();
            await Assert.That(persisted.HumanVerificationEvaluationEnabled).IsFalse();
            await Assert.That(persisted.HumanVerificationCapSecretCiphertext)
                .IsNotNull();
            await Assert.That(persisted.HumanVerificationCapSecretCiphertext!)
                .IsNotEquivalentTo(Encoding.UTF8.GetBytes("cap-secret"));
            await Assert.That(persisted.HumanVerificationTurnstileSecretCiphertext)
                .IsNotNull();
            await Assert.That(persisted.HumanVerificationTurnstileSecretCiphertext!)
                .IsNotEquivalentTo(Encoding.UTF8.GetBytes("turnstile-secret"));

            var runtime = await store.GetRuntimeConfigurationAsync(cancellationToken);
            await Assert.That(runtime.Enabled).IsTrue();
            await Assert.That(runtime.RuntimeEnabled).IsFalse();
            await Assert.That(runtime.EvaluationEnabled).IsFalse();
            await Assert.That(runtime.IsRequired(HumanVerificationAction.Runtime)).IsFalse();
            await Assert.That(runtime.IsRequired(HumanVerificationAction.Evaluation)).IsFalse();
            await Assert.That(runtime.IsRequired(HumanVerificationAction.Login)).IsTrue();
            await Assert.That(runtime.Options.Provider)
                .IsEqualTo(HumanVerificationProvider.Cap);
            await Assert.That(runtime.Options.Cap.Secret).IsEqualTo("cap-secret");
            await Assert.That(runtime.Options.CapApiEndpoint().AbsoluteUri)
                .IsEqualTo("https://cap.example.test/root/site-key/");
            await Assert.That(runtime.Options.CapBackendApiEndpoint().AbsoluteUri)
                .IsEqualTo("http://noctf-cap:3000/site-key/");

            var cached = await cacheProvider
                .GetCache(NoCtfCacheNames.ReadModels)
                .GetOrDefaultAsync<HumanVerificationConfigurationSnapshot>(
                    "human-verification-configuration",
                    token: cancellationToken);
            var cachedJson = JsonSerializer.Serialize(cached);
            await Assert.That(cached).IsNotNull();
            await Assert.That(cachedJson).DoesNotContain("cap-secret");
            await Assert.That(cachedJson).DoesNotContain("turnstile-secret");
        });
    }
}
