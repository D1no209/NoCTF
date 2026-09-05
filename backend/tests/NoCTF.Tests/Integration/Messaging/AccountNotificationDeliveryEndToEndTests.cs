using System.Text.Json;
using DotNet.Testcontainers.Builders;
using JasperFx;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Worker;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Nats;
using Wolverine.Postgresql;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[NotInParallel]
public sealed class AccountNotificationDeliveryEndToEndTests
{
    private const ushort MailpitSmtpPort = 1025;
    private const ushort MailpitHttpPort = 8025;

    [Test]
    [Timeout(300_000)]
    public async Task Registration_reset_and_password_change_flow_through_outbox_NATS_worker_and_SMTP(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase($"noctf_account_delivery_{Guid.NewGuid():N}")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await using var nats = new ContainerBuilder(
                    "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, assignRandomHostPort: true)
                .WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(4222))
                .Build();
            await using var mailpit = new ContainerBuilder("axllent/mailpit:v1.30.4")
                .WithEnvironment("MP_DISABLE_VERSION_CHECK", "true")
                .WithEnvironment("MP_SMTP_AUTH_ACCEPT_ANY", "true")
                .WithEnvironment("MP_SMTP_AUTH_ALLOW_INSECURE", "true")
                .WithPortBinding(MailpitSmtpPort, assignRandomHostPort: true)
                .WithPortBinding(MailpitHttpPort, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(MailpitSmtpPort)
                    .UntilInternalTcpPortIsAvailable(MailpitHttpPort))
                .Build();
            await Task.WhenAll(
                postgres.StartAsync(cancellationToken),
                nats.StartAsync(cancellationToken),
                mailpit.StartAsync(cancellationToken));

            var connectionString = postgres.GetConnectionString();
            var natsConnectionString =
                $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}";
            var suffix = Guid.NewGuid().ToString("N");
            var subject = $"noctf.tests.account-email.{suffix}";
            var stream = $"NOCTF_ACCOUNT_EMAIL_{suffix.ToUpperInvariant()}";
            var consumer = $"noctf-account-email-{suffix}";
            var configuration = new FixedEmailConfiguration(
                mailpit.Hostname,
                mailpit.GetMappedPublicPort(MailpitSmtpPort));
            var dbOptions = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention()
                .Options;
            await using (var setup = new NoCtfDbContext(dbOptions))
                await setup.Database.EnsureCreatedAsync(cancellationToken);

            using var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IEmailVerificationConfigurationStore>(configuration);
                    services.AddSingleton<IEmailVerificationDeliveryConfigurationReader>(configuration);
                    services.AddSingleton<IEmailVerificationSmtpClientFactory,
                        EmailVerificationSmtpClientFactory>();
                    services.AddSingleton<IPasswordHasher<User>>(
                        new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
                        {
                            IterationCount = 10_000
                        })));
                    services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(options =>
                        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
                    services.AddScoped<ITransactionalMessageOutbox,
                        WolverineTransactionalMessageOutbox>();
                    services.AddScoped<IUserAuthenticationStore, AuthenticationStore>();
                    services.AddScoped<IUserRegistrationStore, AuthenticationStore>();
                    services.AddScoped<IEmailVerificationStore, EmailVerificationStore>();
                    services.AddScoped<IPasswordResetStore, PasswordResetStore>();
                    services.AddScoped<IEmailVerificationDelivery,
                        SmtpEmailVerificationDelivery>();
                    services.AddScoped<IPasswordResetEmailDelivery,
                        SmtpEmailVerificationDelivery>();
                    services.AddTransient<AccountNotificationMessageHandler>();
                })
                .UseWolverine(options =>
                {
                    options.Discovery.DisableConventionalDiscovery();
                    options.Discovery.IncludeType(typeof(AccountNotificationMessageHandler));
                    options.PersistMessagesWithPostgresql(
                        connectionString,
                        "wolverine_account_email");
                    options.UseEntityFrameworkCoreTransactions();
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.All;
                    options.UseNats(natsConnectionString)
                        .AutoProvision()
                        .UseJetStream(_ => { })
                        .DefineWorkQueueStream(
                            stream,
                            streamConfiguration => streamConfiguration.WithSubjects(subject),
                            subject);
                    options.ListenToNatsSubject(subject)
                        .UseJetStream(stream, consumer)
                        .Named(consumer)
                        .MaximumParallelMessages(1)
                        .UseDurableInbox();
                    options.PublishMessage<SendEmailVerification>().ToNatsSubject(subject);
                    options.PublishMessage<SendPasswordReset>().ToNatsSubject(subject);
                    options.PublishMessage<SendPasswordChangedNotification>().ToNatsSubject(subject);
                })
                .Build();
            await host.StartAsync(cancellationToken);
            try
            {
                using var scope = host.Services.CreateScope();
                var users = scope.ServiceProvider.GetRequiredService<IUserAuthenticationStore>();
                var registrations = scope.ServiceProvider
                    .GetRequiredService<IUserRegistrationStore>();
                var verification = scope.ServiceProvider
                    .GetRequiredService<IEmailVerificationStore>();
                var passwordReset = scope.ServiceProvider.GetRequiredService<IPasswordResetStore>();
                var now = DateTimeOffset.UtcNow;
                var registration = await new RegisterUser(registrations).ExecuteAsync(
                    new("DeliveryOwner", "delivery-owner@example.test", "old-pass", now),
                    cancellationToken);
                await Assert.That(registration.Succeeded).IsTrue();
                await Assert.That(registration.Value!.VerificationEmailQueued).IsTrue();
                await WaitForMessageCountAsync(mailpit, 1, cancellationToken);

                var userId = registration.Value.Profile.Id;
                await Assert.That(await verification.IssueAsync(
                        userId,
                        now.AddSeconds(30),
                        cancellationToken))
                    .IsEqualTo(EmailVerificationState.RateLimited);
                await Assert.That(await MessageCountAsync(mailpit, cancellationToken))
                    .IsEqualTo(1);

                await Assert.That(await verification.IssueByEmailAsync(
                        "DELIVERY-OWNER@example.test",
                        now.AddMinutes(2),
                        cancellationToken))
                    .IsEqualTo(EmailVerificationState.Issued);
                await WaitForMessageCountAsync(mailpit, 2, cancellationToken);
                await Assert.That(await verification.IssueAsync(
                        userId,
                        now.AddMinutes(4),
                        cancellationToken))
                    .IsEqualTo(EmailVerificationState.Issued);
                await WaitForMessageCountAsync(mailpit, 3, cancellationToken);

                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                var user = await db.Users.SingleAsync(
                    candidate => candidate.Id == userId,
                    cancellationToken);
                user.EmailVerifiedAt = now.AddMinutes(1);
                await db.SaveChangesAsync(cancellationToken);

                await Assert.That(await passwordReset.IssueAsync(
                        "delivery-owner@example.test",
                        now.AddMinutes(5),
                        cancellationToken))
                    .IsEqualTo(PasswordResetRequestState.Queued);
                await WaitForMessageCountAsync(mailpit, 4, cancellationToken);

                await Assert.That(await users.ChangePasswordAsync(
                        userId,
                        "old-pass",
                        "new-pass",
                        now.AddMinutes(6),
                        cancellationToken))
                    .IsEqualTo(ChangePasswordState.Changed);
                await WaitForMessageCountAsync(mailpit, 5, cancellationToken);

                await Assert.That(await db.AccountTokens.CountAsync(
                        token => token.UserId == userId,
                        cancellationToken))
                    .IsEqualTo(4);
                await Assert.That(await db.AccountTokens.CountAsync(
                        token => token.UserId == userId
                            && token.Kind == AccountTokenKind.EmailVerification
                            && token.ConsumedAt == null
                            && token.ExpiresAt > now.AddMinutes(4),
                        cancellationToken))
                    .IsEqualTo(1);
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    private static async Task WaitForMessageCountAsync(
        DotNet.Testcontainers.Containers.IContainer mailpit,
        int expected,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (await MessageCountAsync(mailpit, cancellationToken) >= expected)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        throw new TimeoutException($"Mailpit did not receive {expected} messages.");
    }

    private static async Task<int> MessageCountAsync(
        DotNet.Testcontainers.Containers.IContainer mailpit,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri(
                $"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(MailpitHttpPort)}")
        };
        using var response = await client.GetAsync("/api/v1/messages", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            body,
            cancellationToken: cancellationToken);
        return document.RootElement.GetProperty("total").GetInt32();
    }

    private sealed class FixedEmailConfiguration(string smtpHost, int smtpPort)
        : IEmailVerificationConfigurationStore,
            IEmailVerificationDeliveryConfigurationReader
    {
        private readonly EmailVerificationDeliveryConfiguration delivery = new(
            Enabled: true,
            PublicBaseUrl: "https://noctf.test",
            SmtpHost: smtpHost,
            SmtpPort: smtpPort,
            SmtpSecurityMode: SmtpSecurityMode.None,
            SmtpUserName: "mailer",
            SmtpPassword: "secret",
            SmtpFromAddress: "no-reply@noctf.test",
            SmtpFromName: "NoCTF",
            SmtpTimeoutSeconds: 10);

        public Task<EmailVerificationConfigurationView> GetAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new EmailVerificationConfigurationView(
                Enabled: true,
                PublicBaseUrl: delivery.PublicBaseUrl,
                TokenLifetimeMinutes: 1440,
                ResendCooldownSeconds: 60,
                PasswordResetTokenLifetimeMinutes: 30,
                PasswordResetCooldownSeconds: 60,
                PasswordResetMaxRequestsPerHour: 3,
                SmtpHost: delivery.SmtpHost,
                SmtpPort: delivery.SmtpPort,
                SmtpSecurityMode: delivery.SmtpSecurityMode,
                SmtpUserName: delivery.SmtpUserName,
                SmtpPasswordConfigured: true,
                SmtpFromAddress: delivery.SmtpFromAddress,
                SmtpFromName: delivery.SmtpFromName,
                SmtpTimeoutSeconds: delivery.SmtpTimeoutSeconds,
                UpdatedAt: DateTimeOffset.UnixEpoch));

        public Task<EmailVerificationDeliveryConfiguration?> GetDeliveryConfigurationAsync(
            bool requireEnabled,
            CancellationToken cancellationToken) =>
            Task.FromResult<EmailVerificationDeliveryConfiguration?>(delivery);

        public Task<EmailVerificationConfigurationView> UpdateAsync(
            UpdateEmailVerificationConfigurationCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<EmailVerificationConfigurationView> ReplacePasswordAsync(
            string password,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
