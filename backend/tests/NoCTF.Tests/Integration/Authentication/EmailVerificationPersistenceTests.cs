using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
[NotInParallel]
public sealed class EmailVerificationPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_anonymous_requests_issue_only_one_active_token(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = OptionsFor(postgres);
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.CreateVersion7(now);

            await using (var setupDb = new NoCtfDbContext(options))
            {
                await setupDb.Database.EnsureCreatedAsync(cancellationToken);
                setupDb.Users.Add(new User
                {
                    Id = userId,
                    UserName = "VerificationOwner",
                    NormalizedUserName = "VERIFICATIONOWNER",
                    Email = "verification-owner@example.test",
                    PasswordHash = "unused",
                    Kind = UserKind.Human,
                    Role = UserRole.User,
                    AccountStatus = UserAccountStatus.Active,
                    EmailVerifiedAt = null,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await setupDb.SaveChangesAsync(cancellationToken);
            }

            var firstOutbox = new RecordingOutbox();
            var secondOutbox = new RecordingOutbox();
            await using var firstDb = new NoCtfDbContext(options);
            await using var secondDb = new NoCtfDbContext(options);
            var results = await Task.WhenAll(
                CreateStore(firstDb, firstOutbox).IssueByEmailAsync(
                    " VERIFICATION-OWNER@example.test ",
                    now.AddMinutes(1),
                    cancellationToken),
                CreateStore(secondDb, secondOutbox).IssueByEmailAsync(
                    "verification-owner@example.test",
                    now.AddMinutes(1),
                    cancellationToken));

            await Assert.That(results.Count(result =>
                result == EmailVerificationState.Issued)).IsEqualTo(1);
            await Assert.That(results.Count(result =>
                result == EmailVerificationState.RateLimited)).IsEqualTo(1);
            await Assert.That(firstOutbox.Messages.Concat(secondOutbox.Messages)
                .OfType<SendEmailVerification>()
                .Count()).IsEqualTo(1);

            await using var inspectionDb = new NoCtfDbContext(options);
            var tokens = await inspectionDb.AccountTokens.AsNoTracking()
                .Where(token => token.UserId == userId
                    && token.Kind == AccountTokenKind.EmailVerification)
                .ToListAsync(cancellationToken);
            await Assert.That(tokens.Count).IsEqualTo(1);
            await Assert.That(tokens[0].ConsumedAt).IsNull();
            await Assert.That(tokens[0].InvalidatedAt).IsNull();
        });
    }

    private static EmailVerificationStore CreateStore(
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox) =>
        new(db, new FixedConfigurationStore(), outbox);

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder(
            "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase($"noctf_email_verification_{Guid.NewGuid():N}")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);
        return postgres;
    }

    private static DbContextOptions<NoCtfDbContext> OptionsFor(
        PostgreSqlContainer postgres) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

    private sealed class FixedConfigurationStore : IEmailVerificationConfigurationStore
    {
        public Task<EmailVerificationConfigurationView> GetAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new EmailVerificationConfigurationView(
                Enabled: true,
                PublicBaseUrl: "https://noctf.test",
                TokenLifetimeMinutes: 1440,
                ResendCooldownSeconds: 60,
                PasswordResetTokenLifetimeMinutes: 30,
                PasswordResetCooldownSeconds: 60,
                PasswordResetMaxRequestsPerHour: 3,
                SmtpHost: "smtp.noctf.test",
                SmtpPort: 465,
                SmtpSecurityMode: SmtpSecurityMode.SslOnConnect,
                SmtpUserName: "mailer",
                SmtpPasswordConfigured: true,
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

        public ValueTask ScheduleToRunnerNodeAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
