using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
[NotInParallel]
public sealed class SmtpEmailVerificationDeliveryTests
{
    private const ushort SmtpPort = 3025;
    private const ushort SmtpsPort = 3465;
    private const ushort ImapPort = 3143;
    private const ushort MailpitSmtpPort = 1025;
    private static readonly Guid UserId = Guid.Parse("62a0c83d-3249-43f7-af40-e9fc79df7712");

    [Test]
    [Timeout(300_000)]
    public async Task Mfa_recovery_mail_uses_the_real_Nuxt_route_and_security_mail_contains_no_recovery_material(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var mail = BuildGreenMail(authenticationDisabled: false); await mail.StartAsync(ct);
            var configuration = Configuration(mail.GetMappedPublicPort(SmtpsPort), SmtpSecurityMode.SslOnConnect, "mailer", "secret") with { Enabled = false };
            var delivery = CreateDelivery(configuration); var token = new string('x', 43);
            await Assert.That(await delivery.SendRecoveryAsync(UserId, token, ct)).IsEqualTo(NoCTF.Application.Authentication.Mfa.MfaMailDeliveryState.Sent);
            await Assert.That(await delivery.SendSecurityNotificationAsync(UserId, NoCTF.Domain.Identity.Mfa.MfaOperation.RebindTotp, ct)).IsEqualTo(NoCTF.Application.Authentication.Mfa.MfaMailDeliveryState.Sent);
            var messages = await ReadMessagesAsync(mail, ct); await Assert.That(messages.Count).IsEqualTo(2);
            var recovery = messages.Single(value => value.Subject == "Recover your NoCTF authenticator");
            await Assert.That(recovery.TextBody).Contains($"https://noctf.test/auth/mfa/recovery?token={token}");
            await Assert.That(recovery.HtmlBody).Contains($"https://noctf.test/auth/mfa/recovery?token={token}");
            var security = messages.Single(value => value.Subject == "NoCTF account security changed");
            await Assert.That(security.TextBody).DoesNotContain(token);
            await Assert.That(security.HtmlBody).DoesNotContain(token);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Password_reset_and_change_notice_work_when_registration_verification_is_disabled(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var greenMail = BuildGreenMail(authenticationDisabled: false);
            await greenMail.StartAsync(cancellationToken);
            var configuration = Configuration(
                greenMail.GetMappedPublicPort(SmtpsPort),
                SmtpSecurityMode.SslOnConnect,
                "mailer",
                "secret") with { Enabled = false };
            var delivery = CreateDelivery(configuration);

            var resetState = await delivery.SendResetAsync(
                UserId,
                "single-use-reset-token",
                cancellationToken);
            var changedState = await delivery.SendChangedNotificationAsync(
                UserId,
                cancellationToken);

            await Assert.That(resetState).IsEqualTo(PasswordResetEmailDeliveryState.Sent);
            await Assert.That(changedState).IsEqualTo(PasswordResetEmailDeliveryState.Sent);
            var messages = await ReadMessagesAsync(greenMail, cancellationToken);
            await Assert.That(messages.Count).IsEqualTo(2);
            var reset = messages.Single(message =>
                message.Subject == "Reset your NoCTF password");
            var changed = messages.Single(message =>
                message.Subject == "Your NoCTF password was changed");
            await Assert.That(reset.TextBody).Contains(
                "https://noctf.test/auth/password-reset?token=single-use-reset-token");
            await Assert.That(reset.HtmlBody).Contains(
                "https://noctf.test/auth/password-reset?token=single-use-reset-token");
            await Assert.That(changed.TextBody).DoesNotContain("single-use-reset-token");
            await Assert.That(changed.TextBody).Contains("existing sessions were signed out");
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task GreenMail_accepts_StartTls_and_SslOnConnect_with_utf8_alternative_bodies(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            using var tlsMaterial = new TemporaryTlsMaterial();
            await using var mailpit = BuildMailpit(tlsMaterial.DirectoryPath);
            await mailpit.StartAsync(cancellationToken);
            var startTls = Configuration(
                mailpit.GetMappedPublicPort(MailpitSmtpPort),
                SmtpSecurityMode.StartTls,
                string.Empty,
                null);
            var firstState = await CreateDelivery(startTls).SendVerificationAsync(
                UserId,
                "token-one",
                cancellationToken);
            await Assert.That(firstState).IsEqualTo(EmailVerificationDeliveryState.Sent);

            await using var greenMail = BuildGreenMail(authenticationDisabled: false);
            await greenMail.StartAsync(cancellationToken);
            var sslOnConnect = Configuration(
                greenMail.GetMappedPublicPort(SmtpsPort),
                SmtpSecurityMode.SslOnConnect,
                "mailer",
                "secret");
            var secondState = await CreateDelivery(sslOnConnect).SendVerificationAsync(
                UserId,
                "token-two",
                cancellationToken);
            await Assert.That(secondState).IsEqualTo(EmailVerificationDeliveryState.Sent);

            var messages = await ReadMessagesAsync(greenMail, cancellationToken);
            await Assert.That(messages.Count).IsEqualTo(1);
            await Assert.That(messages[0].Subject).IsEqualTo("NoCTF 邮箱验证");
            await Assert.That(messages[0].TextBody).Contains("你好-admin");
            await Assert.That(messages[0].TextBody).Contains("请勿回复");
            await Assert.That(messages[0].TextBody).Contains("token-two");
            await Assert.That(messages[0].HtmlBody).Contains(
                "auth/verify-email?token=token-two");
            await Assert.That(messages[0].Headers["Auto-Submitted"])
                .IsEqualTo("auto-generated");
            await Assert.That(messages[0].Headers["X-Auto-Response-Suppress"])
                .IsEqualTo("All");
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task GreenMail_accepts_no_authentication_and_reports_authentication_failure(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using (var openRelay = BuildGreenMail(authenticationDisabled: true))
            {
                await openRelay.StartAsync(cancellationToken);
                var configuration = Configuration(
                    openRelay.GetMappedPublicPort(SmtpPort),
                    SmtpSecurityMode.None,
                    string.Empty,
                    null);

                var state = await CreateDelivery(configuration).SendTestAsync(
                    UserId,
                    cancellationToken);

                await Assert.That(state).IsEqualTo(EmailVerificationDeliveryState.Sent);
            }

            await using var authenticated = BuildGreenMail(authenticationDisabled: false);
            await authenticated.StartAsync(cancellationToken);
            var invalidCredentials = Configuration(
                authenticated.GetMappedPublicPort(SmtpsPort),
                SmtpSecurityMode.SslOnConnect,
                "mailer",
                "wrong-secret");

            var exception = await CaptureDeliveryFailureAsync(() =>
                CreateDelivery(invalidCredentials).SendTestAsync(UserId, cancellationToken));

            await Assert.That(exception.Failure)
                .IsEqualTo(EmailVerificationDeliveryFailure.AuthenticationFailed);
            await Assert.That(exception.ToString()).DoesNotContain("wrong-secret");
            await Assert.That(exception.ToString()).DoesNotContain("mailer");
        });
    }

    [Test]
    [Timeout(30_000)]
    public async Task Timeout_cancellation_connection_and_rejection_results_are_stable(
        CancellationToken cancellationToken)
    {
        await using (var stalled = new StalledSmtpServer())
        {
            var timedOut = await CaptureDeliveryFailureAsync(() =>
                CreateDelivery(Configuration(
                    stalled.Port,
                    SmtpSecurityMode.None,
                    string.Empty,
                    null) with { SmtpTimeoutSeconds = 1 })
                    .SendTestAsync(UserId, cancellationToken));
            await Assert.That(timedOut.Failure)
                .IsEqualTo(EmailVerificationDeliveryFailure.TimedOut);
            await Assert.That(timedOut.InnerException).IsNotNull();
            await Assert.That(timedOut.InnerException!.Message)
                .Contains("Timeout");
        }

        await using (var stalled = new StalledSmtpServer())
        {
            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            cancellationSource.CancelAfter(TimeSpan.FromMilliseconds(100));
            var canceled = false;
            try
            {
                await CreateDelivery(Configuration(
                    stalled.Port,
                    SmtpSecurityMode.None,
                    string.Empty,
                    null) with { SmtpTimeoutSeconds = 30 })
                    .SendTestAsync(UserId, cancellationSource.Token);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            await Assert.That(canceled).IsTrue();
        }

        var unavailablePort = ReserveUnavailablePort();
        var connectionFailure = await CaptureDeliveryFailureAsync(() =>
            CreateDelivery(Configuration(
                unavailablePort,
                SmtpSecurityMode.None,
                string.Empty,
                null)).SendTestAsync(UserId, cancellationToken));
        await Assert.That(connectionFailure.Failure)
            .IsEqualTo(EmailVerificationDeliveryFailure.ConnectionFailed);
        await Assert.That(connectionFailure.InnerException).IsNotNull();
        await Assert.That(connectionFailure.InnerException!.Message)
            .StartsWith("SMTP phase failed with ");

        await using var rejecting = new RejectingSmtpServer();
        var rejection = await CaptureDeliveryFailureAsync(() =>
            CreateDelivery(Configuration(
                rejecting.Port,
                SmtpSecurityMode.None,
                string.Empty,
                null)).SendTestAsync(UserId, cancellationToken));
        await Assert.That(rejection.Failure)
            .IsEqualTo(EmailVerificationDeliveryFailure.MessageRejected);
        await Assert.That(rejection.InnerException).IsNotNull();
        await Assert.That(rejection.InnerException!.Message)
            .Contains(nameof(SmtpCommandException));
        await Assert.That(rejection.InnerException.Message).Contains("status=");
        await Assert.That(rejection.ToString()).DoesNotContain("mailbox unavailable");
    }

    private static IContainer BuildGreenMail(bool authenticationDisabled)
    {
        var options = "-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 "
            + "-Dgreenmail.users=mailer:secret@noctf.test,admin:admin-secret@noctf.test "
            + "-Dgreenmail.users.login=local_part";
        if (authenticationDisabled)
            options += " -Dgreenmail.auth.disabled";

        return new ContainerBuilder("greenmail/standalone:2.1.11")
            .WithEnvironment("GREENMAIL_OPTS", options)
            .WithPortBinding(SmtpPort, true)
            .WithPortBinding(SmtpsPort, true)
            .WithPortBinding(ImapPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilInternalTcpPortIsAvailable(SmtpPort)
                .UntilInternalTcpPortIsAvailable(SmtpsPort)
                .UntilInternalTcpPortIsAvailable(ImapPort))
            .Build();
    }

    private static IContainer BuildMailpit(string tlsDirectory) =>
        new ContainerBuilder("axllent/mailpit:v1.30.4")
            .WithEnvironment("MP_DISABLE_VERSION_CHECK", "true")
            .WithEnvironment("MP_SMTP_TLS_CERT", "/certs/cert.pem")
            .WithEnvironment("MP_SMTP_TLS_KEY", "/certs/key.pem")
            .WithEnvironment("MP_SMTP_REQUIRE_STARTTLS", "true")
            .WithBindMount(tlsDirectory, "/certs", AccessMode.ReadOnly)
            .WithPortBinding(MailpitSmtpPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilInternalTcpPortIsAvailable(MailpitSmtpPort))
            .Build();

    private static SmtpEmailVerificationDelivery CreateDelivery(
        EmailVerificationDeliveryConfiguration configuration)
    {
        var data = SmtpDeliveryTestData.Create(configuration, UserId);
        return new(
            data.Factory,
            data.Secrets,
            new TrustedTestSmtpClientFactory(),
            NullLogger<SmtpEmailVerificationDelivery>.Instance);
    }

    private static EmailVerificationDeliveryConfiguration Configuration(
        int port,
        SmtpSecurityMode securityMode,
        string userName,
        string? password) =>
        new(
            Enabled: true,
            PublicBaseUrl: "https://noctf.test",
            SmtpHost: "127.0.0.1",
            SmtpPort: port,
            SmtpSecurityMode: securityMode,
            SmtpUserName: userName,
            SmtpPassword: password,
            SmtpFromAddress: "no-reply@noctf.test",
            SmtpFromName: "NoCTF 测试",
            SmtpTimeoutSeconds: 10);

    private static async Task<IReadOnlyList<MimeKit.MimeMessage>> ReadMessagesAsync(
        IContainer greenMail,
        CancellationToken cancellationToken)
    {
        using var client = new ImapClient
        {
            ServerCertificateValidationCallback = static (_, _, _, _) => true
        };
        await client.ConnectAsync(
            "127.0.0.1",
            greenMail.GetMappedPublicPort(ImapPort),
            SecureSocketOptions.None,
            cancellationToken);
        await client.AuthenticateAsync("admin", "admin-secret", cancellationToken);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var messages = new List<MimeKit.MimeMessage>(client.Inbox.Count);
        for (var index = 0; index < client.Inbox.Count; index++)
            messages.Add(await client.Inbox.GetMessageAsync(index, cancellationToken));
        await client.DisconnectAsync(true, cancellationToken);
        return messages;
    }

    private static async Task<EmailVerificationDeliveryException> CaptureDeliveryFailureAsync(
        Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (EmailVerificationDeliveryException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected email delivery to fail.");
    }

    private static int ReserveUnavailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class TrustedTestSmtpClientFactory : IEmailVerificationSmtpClientFactory
    {
        public SmtpClient Create() =>
            new()
            {
                ServerCertificateValidationCallback = static (_, _, _, _) => true
            };
    }

    private sealed class TemporaryTlsMaterial : IDisposable
    {
        public TemporaryTlsMaterial()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"noctf-smtp-tls-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=localhost",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: false));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(
                request.PublicKey,
                critical: false));
            using var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllText(
                Path.Combine(DirectoryPath, "cert.pem"),
                certificate.ExportCertificatePem());
            File.WriteAllText(
                Path.Combine(DirectoryPath, "key.pem"),
                key.ExportPkcs8PrivateKeyPem());
        }

        public string DirectoryPath { get; }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }

    private sealed class StalledSmtpServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellationSource = new();
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly Task serverTask;

        public StalledSmtpServer()
        {
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            serverTask = HoldConnectionAsync(cancellationSource.Token);
        }

        public int Port { get; }

        public async ValueTask DisposeAsync()
        {
            await cancellationSource.CancelAsync();
            listener.Stop();
            await IgnoreCancellationAsync(serverTask);
            cancellationSource.Dispose();
        }

        private async Task HoldConnectionAsync(CancellationToken cancellationToken)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class RejectingSmtpServer : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellationSource = new();
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly Task serverTask;

        public RejectingSmtpServer()
        {
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            serverTask = RejectRecipientAsync(cancellationSource.Token);
        }

        public int Port { get; }

        public async ValueTask DisposeAsync()
        {
            await cancellationSource.CancelAsync();
            listener.Stop();
            await IgnoreCancellationAsync(serverTask);
            cancellationSource.Dispose();
        }

        private async Task RejectRecipientAsync(CancellationToken cancellationToken)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            await using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\r\n"
            };
            await writer.WriteLineAsync("220 smtp.noctf.test ESMTP");
            while (await reader.ReadLineAsync(cancellationToken) is { } command)
            {
                if (command.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250-smtp.noctf.test");
                    await writer.WriteLineAsync("250 PIPELINING");
                }
                else if (command.StartsWith("HELO", StringComparison.OrdinalIgnoreCase)
                    || command.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 OK");
                }
                else if (command.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("550 mailbox unavailable");
                }
                else if (command.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 bye");
                    return;
                }
                else
                {
                    await writer.WriteLineAsync("250 OK");
                }
            }
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
    }
}
