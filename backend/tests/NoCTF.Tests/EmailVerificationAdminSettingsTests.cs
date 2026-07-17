using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.API.Auth;
using NoCTF.Infrastructure;

namespace NoCTF.Tests;

public class EmailVerificationAdminSettingsTests
{
    [Fact]
    public async Task Update_ProtectsPasswordAndNeverReturnsIt()
    {
        await using var db = CreateDb();
        var store = CreateStore(db);

        var view = await store.UpdateAsync(CreateUpdate("smtp-password-value"), CancellationToken.None);
        var stored = await db.EmailVerificationSettings.SingleAsync();

        Assert.True(view.SmtpPasswordConfigured);
        Assert.DoesNotContain("smtp-password-value", stored.SmtpPasswordProtected, StringComparison.Ordinal);
        Assert.StartsWith("v1:", stored.SmtpPasswordProtected, StringComparison.Ordinal);
        Assert.Null(typeof(EmailVerificationSettingsView).GetProperty("SmtpPassword"));
        Assert.Equal("smtp-password-value", (await store.GetAsync(CancellationToken.None)).Smtp.Password);
    }

    [Fact]
    public async Task Update_BlankPasswordRetainsProtectedCredential()
    {
        await using var db = CreateDb();
        var store = CreateStore(db);
        await store.UpdateAsync(CreateUpdate("original-password"), CancellationToken.None);
        var originalProtected = (await db.EmailVerificationSettings.SingleAsync()).SmtpPasswordProtected;

        var update = CreateUpdate(string.Empty);
        update.SmtpFromName = "Updated sender";
        await store.UpdateAsync(update, CancellationToken.None);

        var stored = await db.EmailVerificationSettings.SingleAsync();
        Assert.NotEqual(string.Empty, stored.SmtpPasswordProtected);
        Assert.NotEqual(originalProtected, stored.SmtpPasswordProtected);
        Assert.Equal("original-password", (await store.GetAsync(CancellationToken.None)).Smtp.Password);
        Assert.Equal("Updated sender", stored.SmtpFromName);
    }

    [Fact]
    public async Task Update_RejectsInvalidEnabledConfiguration()
    {
        await using var db = CreateDb();
        var store = CreateStore(db);
        var update = CreateUpdate("password");
        update.PublicBaseUrl = "file:///etc/passwd";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.UpdateAsync(update, CancellationToken.None));
        Assert.Empty(await db.EmailVerificationSettings.ToListAsync());
    }

    private static EmailVerificationSettingsUpdate CreateUpdate(string password) => new()
    {
        Enabled = true,
        PublicBaseUrl = "http://localhost:5173",
        TokenLifetimeMinutes = 60,
        ResendCooldownSeconds = 60,
        SmtpHost = "smtp.example.test",
        SmtpPort = 587,
        SmtpEnableSsl = true,
        SmtpUserName = "mailer",
        SmtpPassword = password,
        SmtpFromAddress = "no-reply@example.test",
        SmtpFromName = "NoCTF",
        SmtpTimeoutSeconds = 10
    };

    private static EmailVerificationSettingsStore CreateStore(ApplicationDbContext db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = new string('j', 64)
            })
            .Build();
        return new EmailVerificationSettingsStore(
            db,
            Options.Create(new EmailVerificationOptions()),
            configuration,
            new TestHostEnvironment(),
            NullLogger<EmailVerificationSettingsStore>.Instance);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new EmailVerificationSettingsTenantContext());
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "NoCTF.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

file sealed class EmailVerificationSettingsTenantContext : ITenantContext
{
    public Guid? CompetitionId => null;
    public void SetCompetitionId(Guid? id) { }
}
