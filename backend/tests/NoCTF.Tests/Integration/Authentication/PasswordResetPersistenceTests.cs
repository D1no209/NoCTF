using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
[NotInParallel]
public sealed class PasswordResetPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_request_and_completion_are_single_winner_and_invalidate_sessions(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = OptionsFor(postgres);
            var hasher = CreateHasher();
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.CreateVersion7(now);

            await using (var setupDb = new NoCtfDbContext(options))
            {
                await setupDb.Database.EnsureCreatedAsync(cancellationToken);
                await new AuthenticationStore(setupDb, hasher).CreateAsync(
                    userId,
                    "ResetOwner",
                    "reset-owner@example.test",
                    "old-pass",
                    emailVerified: true,
                    now,
                    cancellationToken);
            }

            var firstOutbox = new RecordingOutbox();
            var secondOutbox = new RecordingOutbox();
            await using var firstIssueDb = new NoCtfDbContext(options);
            await using var secondIssueDb = new NoCtfDbContext(options);
            var issueResults = await Task.WhenAll(
                CreateStore(firstIssueDb, hasher, firstOutbox).IssueAsync(
                    "  RESET-OWNER@example.test ", now.AddMinutes(1), cancellationToken),
                CreateStore(secondIssueDb, hasher, secondOutbox).IssueAsync(
                    "reset-owner@example.test", now.AddMinutes(1), cancellationToken));

            await Assert.That(issueResults.Count(result =>
                result == PasswordResetRequestState.Queued)).IsEqualTo(1);
            await Assert.That(issueResults.Count(result =>
                result == PasswordResetRequestState.RateLimited)).IsEqualTo(1);
            var resetMessage = firstOutbox.Messages.Concat(secondOutbox.Messages)
                .OfType<SendPasswordReset>()
                .Single();

            await using (var inspectionDb = new NoCtfDbContext(options))
            {
                var persistedToken = await inspectionDb.AccountTokens.AsNoTracking()
                    .SingleAsync(token => token.Kind == AccountTokenKind.PasswordReset,
                        cancellationToken);
                var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(resetMessage.Token));
                await Assert.That(persistedToken.TokenSha256).IsEquivalentTo(expectedHash);
                await Assert.That(Convert.ToHexString(persistedToken.TokenSha256))
                    .DoesNotContain(resetMessage.Token);
                await Assert.That(persistedToken.ExpiresAt.ToUnixTimeMilliseconds())
                    .IsEqualTo(now.AddMinutes(31).ToUnixTimeMilliseconds());
            }

            var firstCompletionOutbox = new RecordingOutbox();
            var secondCompletionOutbox = new RecordingOutbox();
            await using var firstCompletionDb = new NoCtfDbContext(options);
            await using var secondCompletionDb = new NoCtfDbContext(options);
            var completionResults = await Task.WhenAll(
                CreateStore(firstCompletionDb, hasher, firstCompletionOutbox).CompleteAsync(
                    resetMessage.Token, "new-pass", now.AddMinutes(2), cancellationToken),
                CreateStore(secondCompletionDb, hasher, secondCompletionOutbox).CompleteAsync(
                    resetMessage.Token, "other-pass", now.AddMinutes(2), cancellationToken));

            await Assert.That(completionResults.Count(result =>
                result == PasswordResetCompletionState.Reset)).IsEqualTo(1);
            await Assert.That(completionResults.Count(result =>
                result == PasswordResetCompletionState.InvalidOrExpired)).IsEqualTo(1);
            await Assert.That(firstCompletionOutbox.Messages
                .Concat(secondCompletionOutbox.Messages)
                .OfType<SendPasswordChangedNotification>()
                .Count()).IsEqualTo(1);

            await using var verificationDb = new NoCtfDbContext(options);
            var users = new AuthenticationStore(verificationDb, hasher);
            var user = await users.FindByIdAsync(userId, cancellationToken);
            await Assert.That(user!.TokenVersion).IsEqualTo(1);
            await Assert.That(await new AccessTokenVersionReader(verificationDb).IsCurrentAsync(
                userId, tokenVersion: 0, cancellationToken)).IsFalse();
            var winningPassword = completionResults[0] == PasswordResetCompletionState.Reset
                ? "new-pass"
                : "other-pass";
            await Assert.That(await users.VerifyPasswordAsync(
                userId, winningPassword, cancellationToken)).IsTrue();
            var consumed = await verificationDb.AccountTokens.AsNoTracking()
                .SingleAsync(token => token.Kind == AccountTokenKind.PasswordReset,
                    cancellationToken);
            await Assert.That(consumed.ConsumedAt!.Value.ToUnixTimeMilliseconds())
                .IsEqualTo(now.AddMinutes(2).ToUnixTimeMilliseconds());

            var laterOutbox = new RecordingOutbox();
            var laterStore = CreateStore(verificationDb, hasher, laterOutbox);
            var secondIssue = await laterStore.IssueAsync(
                "reset-owner@example.test", now.AddMinutes(3), cancellationToken);
            var thirdIssue = await laterStore.IssueAsync(
                "reset-owner@example.test", now.AddMinutes(4), cancellationToken);
            var fourthIssue = await laterStore.IssueAsync(
                "reset-owner@example.test", now.AddMinutes(5), cancellationToken);

            await Assert.That(secondIssue).IsEqualTo(PasswordResetRequestState.Queued);
            await Assert.That(thirdIssue).IsEqualTo(PasswordResetRequestState.Queued);
            await Assert.That(fourthIssue).IsEqualTo(PasswordResetRequestState.RateLimited);
            var laterTokens = await verificationDb.AccountTokens.AsNoTracking()
                .Where(token => token.Kind == AccountTokenKind.PasswordReset)
                .OrderBy(candidate => candidate.CreatedAt)
                .ToListAsync(cancellationToken);
            await Assert.That(laterTokens.Count).IsEqualTo(3);
            await Assert.That(laterTokens[1].InvalidatedAt!.Value.ToUnixTimeMilliseconds())
                .IsEqualTo(now.AddMinutes(4).ToUnixTimeMilliseconds());
            await Assert.That(laterTokens[2].InvalidatedAt).IsNull();
            await Assert.That(laterOutbox.Messages.OfType<SendPasswordReset>().Count())
                .IsEqualTo(2);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Only_active_verified_human_accounts_receive_reset_tokens(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = OptionsFor(postgres);
            var now = DateTimeOffset.UtcNow;
            var hasher = CreateHasher();
            var outbox = new RecordingOutbox();

            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            db.Users.AddRange(
                CreateUser("unverified", UserKind.Human, UserAccountStatus.Active, false, now),
                CreateUser("bot", UserKind.Bot, UserAccountStatus.Active, true, now),
                CreateUser("banned", UserKind.Human, UserAccountStatus.Banned, true, now),
                CreateUser("disabled", UserKind.Human, UserAccountStatus.Disabled, true, now),
                CreateUser("anonymized", UserKind.Human, UserAccountStatus.Anonymized, true, now));
            await db.SaveChangesAsync(cancellationToken);
            var store = CreateStore(db, hasher, outbox);

            foreach (var email in new[]
            {
                "unverified@example.test",
                "bot@example.test",
                "banned@example.test",
                "disabled@example.test",
                "anonymized@example.test",
                "missing@example.test"
            })
            {
                var result = await store.IssueAsync(email, now.AddMinutes(1), cancellationToken);
                await Assert.That(result).IsEqualTo(PasswordResetRequestState.Ignored);
            }

            await Assert.That(await db.AccountTokens.CountAsync(
                    token => token.Kind == AccountTokenKind.PasswordReset,
                    cancellationToken))
                .IsEqualTo(0);
            await Assert.That(outbox.Messages).IsEmpty();
        });
    }

    private static User CreateUser(
        string name,
        UserKind kind,
        UserAccountStatus status,
        bool verified,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            PasswordHash = "unused",
            Kind = kind,
            Role = UserRole.User,
            AccountStatus = status,
            EmailVerifiedAt = verified ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static PasswordResetStore CreateStore(
        NoCtfDbContext db,
        IPasswordHasher<User> hasher,
        ITransactionalMessageOutbox outbox) =>
        new(
            db,
            hasher,
            new FixedConfigurationStore(),
            new FixedDeliveryConfigurationReader(),
            outbox);

    private static PasswordHasher<User> CreateHasher() =>
        new(Options.Create(new PasswordHasherOptions { IterationCount = 10_000 }));

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase($"noctf_password_reset_{Guid.NewGuid():N}")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);
        return postgres;
    }

    private static DbContextOptions<NoCtfDbContext> OptionsFor(PostgreSqlContainer postgres) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

    private sealed class FixedConfigurationStore : IEmailVerificationConfigurationStore
    {
        public Task<EmailVerificationConfigurationView> GetAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new EmailVerificationConfigurationView(
                Enabled: false,
                PublicBaseUrl: "https://noctf.test",
                TokenLifetimeMinutes: 1440,
                ResendCooldownSeconds: 60,
                PasswordResetTokenLifetimeMinutes: 30,
                PasswordResetCooldownSeconds: 60,
                PasswordResetMaxRequestsPerHour: 3,
                SmtpHost: "smtp.noctf.test",
                SmtpPort: 465,
                SmtpSecurityMode: SmtpSecurityMode.SslOnConnect,
                SmtpUserName: string.Empty,
                SmtpPasswordConfigured: false,
                SmtpFromAddress: "no-reply@noctf.test",
                SmtpFromName: "NoCTF",
                SmtpTimeoutSeconds: 10,
                UpdatedAt: DateTimeOffset.UnixEpoch));

        public Task<EmailVerificationConfigurationView> UpdateAsync(
            UpdateEmailVerificationConfigurationCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<EmailVerificationConfigurationView> ReplacePasswordAsync(
            string password,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedDeliveryConfigurationReader
        : IEmailVerificationDeliveryConfigurationReader
    {
        public Task<EmailVerificationDeliveryConfiguration?> GetDeliveryConfigurationAsync(
            bool requireEnabled,
            CancellationToken cancellationToken) =>
            Task.FromResult<EmailVerificationDeliveryConfiguration?>(new(
                Enabled: false,
                PublicBaseUrl: "https://noctf.test",
                SmtpHost: "smtp.noctf.test",
                SmtpPort: 465,
                SmtpSecurityMode: SmtpSecurityMode.SslOnConnect,
                SmtpUserName: string.Empty,
                SmtpPassword: null,
                SmtpFromAddress: "no-reply@noctf.test",
                SmtpFromName: "NoCTF",
                SmtpTimeoutSeconds: 10));
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        private readonly ConcurrentQueue<object> messages = new();

        public IReadOnlyCollection<object> Messages => messages.ToArray();

        public ValueTask PublishAsync<T>(T message)
        {
            messages.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
