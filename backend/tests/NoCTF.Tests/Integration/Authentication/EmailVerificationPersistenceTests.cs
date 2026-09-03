using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    public async Task Registration_commits_user_token_and_email_envelope_in_one_transaction(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = OptionsFor(postgres);
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.CreateVersion7(now);

            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var outbox = new RecordingOutbox(db);
            var registrations = new AuthenticationStore(
                db,
                new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
                {
                    IterationCount = 10_000
                })),
                outbox,
                emailVerificationConfiguration: new FixedConfigurationStore());

            var result = await registrations.RegisterAsync(
                userId,
                "RegistrationOwner",
                "registration-owner@example.test",
                "eight888",
                now,
                cancellationToken);

            await Assert.That(result.UserState).IsEqualTo(CreateUserState.Created);
            await Assert.That(result.VerificationState)
                .IsEqualTo(EmailVerificationState.Issued);
            await Assert.That(outbox.Messages.OfType<SendEmailVerification>())
                .HasSingleItem();
            await Assert.That(outbox.TransactionWasActiveWhenPublished).IsTrue();
            await Assert.That(outbox.FlushCount).IsEqualTo(1);

            db.ChangeTracker.Clear();
            var user = await db.Users.SingleAsync(
                candidate => candidate.Id == userId,
                cancellationToken);
            var token = await db.AccountTokens.SingleAsync(
                candidate => candidate.UserId == userId
                    && candidate.Kind == AccountTokenKind.EmailVerification,
                cancellationToken);
            await Assert.That(user.EmailVerifiedAt).IsNull();
            await Assert.That(token.ConsumedAt).IsNull();
            await Assert.That(token.InvalidatedAt).IsNull();
        });
    }

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

            await using var firstDb = new NoCtfDbContext(options);
            await using var secondDb = new NoCtfDbContext(options);
            var firstOutbox = new RecordingOutbox(firstDb);
            var secondOutbox = new RecordingOutbox(secondDb);
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
            await Assert.That(new[] { firstOutbox, secondOutbox }
                    .Single(outbox => outbox.Messages.OfType<SendEmailVerification>().Any())
                    .TokenWasPendingWhenPublished)
                .IsTrue();

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

    [Test]
    [Timeout(300_000)]
    public async Task Unauthenticated_smtp_can_issue_a_verification_message(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = OptionsFor(postgres);
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.CreateVersion7(now);

            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            db.Users.Add(new User
            {
                Id = userId,
                UserName = "RelayOwner",
                NormalizedUserName = "RELAYOWNER",
                Email = "relay-owner@example.test",
                PasswordHash = "unused",
                Kind = UserKind.Human,
                Role = UserRole.User,
                AccountStatus = UserAccountStatus.Active,
                EmailVerifiedAt = null,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            var outbox = new RecordingOutbox(db);

            var state = await new EmailVerificationStore(
                    db,
                    new FixedConfigurationStore(usesAuthentication: false),
                    outbox)
                .IssueAsync(userId, now.AddMinutes(1), cancellationToken);

            await Assert.That(state).IsEqualTo(EmailVerificationState.Issued);
            await Assert.That(outbox.Messages.OfType<SendEmailVerification>())
                .HasSingleItem();
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

    private sealed class FixedConfigurationStore(bool usesAuthentication = true)
        : IEmailVerificationConfigurationStore
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
                SmtpUserName: usesAuthentication ? "mailer" : string.Empty,
                SmtpPasswordConfigured: usesAuthentication,
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

    private sealed class RecordingOutbox(NoCtfDbContext db) : ITransactionalMessageOutbox
    {
        private readonly ConcurrentQueue<object> messages = new();

        public IReadOnlyCollection<object> Messages => messages.ToArray();
        public bool TokenWasPendingWhenPublished { get; private set; }
        public bool TransactionWasActiveWhenPublished { get; private set; }
        public int FlushCount { get; private set; }

        public ValueTask PublishAsync<T>(T message)
        {
            if (message is SendEmailVerification)
            {
                TransactionWasActiveWhenPublished =
                    db.Database.CurrentTransaction is not null;
                TokenWasPendingWhenPublished = db.ChangeTracker
                    .Entries<AccountToken>()
                    .Any(entry => entry.State == EntityState.Added);
            }
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

        public Task FlushOutgoingMessagesAsync()
        {
            FlushCount++;
            return Task.CompletedTask;
        }
    }
}
