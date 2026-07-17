using System.Net.Mail;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.API.Auth;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Tests;

public class EmailVerificationTests
{
    [Theory]
    [InlineData(465, true, SecureSocketOptions.SslOnConnect)]
    [InlineData(587, true, SecureSocketOptions.StartTls)]
    [InlineData(25, false, SecureSocketOptions.None)]
    public void SmtpTransport_UsesExplicitSecurityMode(
        int port,
        bool enableSsl,
        SecureSocketOptions expected)
    {
        var smtp = new SmtpOptions { Port = port, EnableSsl = enableSsl };

        Assert.Equal(expected, SmtpVerificationEmailSender.GetSocketOptions(smtp));
    }

    [Fact]
    public async Task SendAndVerify_StoresOnlyHashAndVerifiesUser()
    {
        await using var db = CreateDb();
        var user = AddUser(db);
        var sender = new RecordingEmailSender();
        var service = CreateService(db, sender);

        var dispatch = await service.SendForRegistrationAsync(user, CancellationToken.None);
        var rawToken = sender.SingleToken;
        var storedToken = await db.EmailVerificationTokens.SingleAsync();

        Assert.True(dispatch.Required);
        Assert.True(dispatch.Sent);
        Assert.DoesNotContain(rawToken, storedToken.TokenHash, StringComparison.Ordinal);
        Assert.Equal(64, storedToken.TokenHash.Length);

        var result = await service.VerifyAsync(rawToken, CancellationToken.None);

        Assert.Equal(EmailVerificationAttempt.Verified, result);
        Assert.NotNull(user.EmailVerifiedAt);
        Assert.NotNull(storedToken.ConsumedAt);
        Assert.Equal(
            EmailVerificationAttempt.AlreadyVerified,
            await service.VerifyAsync(rawToken, CancellationToken.None));
    }

    [Fact]
    public async Task Verify_RejectsExpiredToken()
    {
        await using var db = CreateDb();
        var user = AddUser(db);
        var sender = new RecordingEmailSender();
        var service = CreateService(db, sender);
        await service.SendForRegistrationAsync(user, CancellationToken.None);
        var token = await db.EmailVerificationTokens.SingleAsync();
        token.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        var result = await service.VerifyAsync(sender.SingleToken, CancellationToken.None);

        Assert.Equal(EmailVerificationAttempt.Expired, result);
        Assert.Null(user.EmailVerifiedAt);
        Assert.NotNull(token.ConsumedAt);
    }

    [Fact]
    public async Task Resend_UsesGenericNoOpDuringCooldown()
    {
        await using var db = CreateDb();
        var user = AddUser(db);
        var sender = new RecordingEmailSender();
        var service = CreateService(db, sender);
        await service.SendForRegistrationAsync(user, CancellationToken.None);

        await service.ResendAsync(user.Email, CancellationToken.None);
        await service.ResendAsync("missing@example.test", CancellationToken.None);

        Assert.Single(sender.VerificationUrls);
        Assert.Single(await db.EmailVerificationTokens.ToListAsync());
    }

    [Fact]
    public async Task DeliveryFailure_ConsumesTokenAndLeavesUserPending()
    {
        await using var db = CreateDb();
        var user = AddUser(db);
        var service = CreateService(db, new ThrowingEmailSender());

        var result = await service.SendForRegistrationAsync(user, CancellationToken.None);

        Assert.True(result.Required);
        Assert.False(result.Sent);
        Assert.Null(user.EmailVerifiedAt);
        Assert.NotNull((await db.EmailVerificationTokens.SingleAsync()).ConsumedAt);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new EmailVerificationTenantContext());
    }

    private static User AddUser(ApplicationDbContext db)
    {
        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "pending-user",
            Email = "pending@example.test",
            PasswordHash = "hash",
            Role = UserRole.User,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static EmailVerificationService CreateService(
        ApplicationDbContext db,
        IVerificationEmailSender sender)
        => new(
            db,
            sender,
            new StaticSettingsStore(new EmailVerificationOptions
            {
                Enabled = true,
                PublicBaseUrl = "https://ctf.example.test",
                TokenLifetimeMinutes = 60,
                ResendCooldownSeconds = 60
            }),
            NullLogger<EmailVerificationService>.Instance);

    private sealed class RecordingEmailSender : IVerificationEmailSender
    {
        public List<string> VerificationUrls { get; } = [];
        public string SingleToken
        {
            get
            {
                var query = new Uri(Assert.Single(VerificationUrls)).Query;
                return Uri.UnescapeDataString(query["?token=".Length..]);
            }
        }

        public Task SendAsync(string recipient, string verificationUrl, CancellationToken ct)
        {
            VerificationUrls.Add(verificationUrl);
            return Task.CompletedTask;
        }

        public Task SendTestAsync(string recipient, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ThrowingEmailSender : IVerificationEmailSender
    {
        public Task SendAsync(string recipient, string verificationUrl, CancellationToken ct)
            => throw new SmtpException("offline");

        public Task SendTestAsync(string recipient, CancellationToken ct)
            => throw new SmtpException("offline");
    }

    private sealed class StaticSettingsStore(EmailVerificationOptions options)
        : IEmailVerificationSettingsStore
    {
        public Task<bool> IsEnabledAsync(CancellationToken ct)
            => Task.FromResult(options.Enabled);

        public Task<EmailVerificationOptions> GetAsync(CancellationToken ct)
            => Task.FromResult(options);

        public Task<EmailVerificationSettingsView> GetViewAsync(CancellationToken ct)
            => throw new NotSupportedException();

        public Task<EmailVerificationSettingsView> UpdateAsync(
            EmailVerificationSettingsUpdate update,
            CancellationToken ct) => throw new NotSupportedException();
    }
}

file sealed class EmailVerificationTenantContext : ITenantContext
{
    public Guid? CompetitionId => null;
    public void SetCompetitionId(Guid? id) { }
}
