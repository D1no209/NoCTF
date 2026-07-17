using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Auth;

public sealed record EmailVerificationSettingsView(
    bool Enabled,
    string PublicBaseUrl,
    int TokenLifetimeMinutes,
    int ResendCooldownSeconds,
    string SmtpHost,
    int SmtpPort,
    bool SmtpEnableSsl,
    string SmtpUserName,
    bool SmtpPasswordConfigured,
    string SmtpFromAddress,
    string SmtpFromName,
    int SmtpTimeoutSeconds,
    bool Persisted,
    DateTime? UpdatedAt);

public sealed class EmailVerificationSettingsUpdate
{
    public bool Enabled { get; set; }
    public string PublicBaseUrl { get; set; } = string.Empty;
    public int TokenLifetimeMinutes { get; set; }
    public int ResendCooldownSeconds { get; set; }
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; }
    public bool SmtpEnableSsl { get; set; }
    public string SmtpUserName { get; set; } = string.Empty;
    public string? SmtpPassword { get; set; }
    public string SmtpFromAddress { get; set; } = string.Empty;
    public string SmtpFromName { get; set; } = string.Empty;
    public int SmtpTimeoutSeconds { get; set; }
}

public sealed record EmailVerificationTestResult(bool Success, string Code);

public interface IEmailVerificationSettingsStore
{
    Task<bool> IsEnabledAsync(CancellationToken ct);
    Task<EmailVerificationOptions> GetAsync(CancellationToken ct);
    Task<EmailVerificationSettingsView> GetViewAsync(CancellationToken ct);
    Task<EmailVerificationSettingsView> UpdateAsync(EmailVerificationSettingsUpdate update, CancellationToken ct);
}

internal sealed class EmailVerificationSettingsStore(
    ApplicationDbContext db,
    IOptions<EmailVerificationOptions> configuredOptions,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<EmailVerificationSettingsStore> logger) : IEmailVerificationSettingsStore
{
    internal static readonly Guid SettingsId = Guid.Parse("4fe5652e-fd46-4f7c-8f28-2c268929be49");
    private readonly EmailVerificationOptions _configuredOptions = configuredOptions.Value;
    private readonly EmailVerificationSecretProtector _protector = new(
        configuration["JwtSettings:Secret"]
        ?? throw new InvalidOperationException("JwtSettings:Secret is required."));

    public async Task<bool> IsEnabledAsync(CancellationToken ct)
    {
        var persisted = await db.EmailVerificationSettings
            .AsNoTracking()
            .Where(item => item.Id == SettingsId)
            .Select(item => (bool?)item.Enabled)
            .FirstOrDefaultAsync(ct);
        return persisted ?? _configuredOptions.Enabled;
    }

    public async Task<EmailVerificationOptions> GetAsync(CancellationToken ct)
    {
        var settings = await db.EmailVerificationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == SettingsId, ct);
        return settings is null ? Clone(_configuredOptions) : ToOptions(settings);
    }

    public async Task<EmailVerificationSettingsView> GetViewAsync(CancellationToken ct)
    {
        var settings = await db.EmailVerificationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == SettingsId, ct);
        var options = settings is null ? Clone(_configuredOptions) : ToOptions(settings);
        return ToView(options, settings);
    }

    public async Task<EmailVerificationSettingsView> UpdateAsync(
        EmailVerificationSettingsUpdate update,
        CancellationToken ct)
    {
        var existing = await db.EmailVerificationSettings
            .FirstOrDefaultAsync(item => item.Id == SettingsId, ct);
        var currentPassword = existing is null
            ? _configuredOptions.Smtp.Password
            : Unprotect(existing.SmtpPasswordProtected);
        var password = string.IsNullOrEmpty(update.SmtpPassword)
            ? currentPassword
            : update.SmtpPassword;

        var options = new EmailVerificationOptions
        {
            Enabled = update.Enabled,
            PublicBaseUrl = update.PublicBaseUrl.Trim(),
            TokenLifetimeMinutes = update.TokenLifetimeMinutes,
            ResendCooldownSeconds = update.ResendCooldownSeconds,
            Smtp = new SmtpOptions
            {
                Host = update.SmtpHost.Trim(),
                Port = update.SmtpPort,
                EnableSsl = update.SmtpEnableSsl,
                UserName = update.SmtpUserName.Trim(),
                Password = password,
                FromAddress = update.SmtpFromAddress.Trim(),
                FromName = update.SmtpFromName.Trim(),
                TimeoutSeconds = update.SmtpTimeoutSeconds
            }
        };
        EmailVerificationOptionsValidator.Validate(options, environment.IsDevelopment());

        var settings = existing ?? new EmailVerificationSettings { Id = SettingsId };
        settings.Enabled = options.Enabled;
        settings.PublicBaseUrl = options.PublicBaseUrl;
        settings.TokenLifetimeMinutes = options.TokenLifetimeMinutes;
        settings.ResendCooldownSeconds = options.ResendCooldownSeconds;
        settings.SmtpHost = options.Smtp.Host;
        settings.SmtpPort = options.Smtp.Port;
        settings.SmtpEnableSsl = options.Smtp.EnableSsl;
        settings.SmtpUserName = options.Smtp.UserName;
        settings.SmtpPasswordProtected = string.IsNullOrEmpty(options.Smtp.Password)
            ? string.Empty
            : _protector.Protect(options.Smtp.Password);
        settings.SmtpFromAddress = options.Smtp.FromAddress;
        settings.SmtpFromName = options.Smtp.FromName;
        settings.SmtpTimeoutSeconds = options.Smtp.TimeoutSeconds;
        settings.UpdatedAt = DateTime.UtcNow;
        if (existing is null)
            db.EmailVerificationSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return ToView(options, settings);
    }

    private EmailVerificationOptions ToOptions(EmailVerificationSettings settings) => new()
    {
        Enabled = settings.Enabled,
        PublicBaseUrl = settings.PublicBaseUrl,
        TokenLifetimeMinutes = settings.TokenLifetimeMinutes,
        ResendCooldownSeconds = settings.ResendCooldownSeconds,
        Smtp = new SmtpOptions
        {
            Host = settings.SmtpHost,
            Port = settings.SmtpPort,
            EnableSsl = settings.SmtpEnableSsl,
            UserName = settings.SmtpUserName,
            Password = Unprotect(settings.SmtpPasswordProtected),
            FromAddress = settings.SmtpFromAddress,
            FromName = settings.SmtpFromName,
            TimeoutSeconds = settings.SmtpTimeoutSeconds
        }
    };

    private string Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
            return string.Empty;
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(
                "Stored email verification SMTP credential could not be decrypted: {ErrorType}",
                exception.InnerException?.GetType().Name ?? exception.GetType().Name);
            return string.Empty;
        }
    }

    private static EmailVerificationSettingsView ToView(
        EmailVerificationOptions options,
        EmailVerificationSettings? settings) => new(
            options.Enabled,
            options.PublicBaseUrl,
            options.TokenLifetimeMinutes,
            options.ResendCooldownSeconds,
            options.Smtp.Host,
            options.Smtp.Port,
            options.Smtp.EnableSsl,
            options.Smtp.UserName,
            settings is null
                ? !string.IsNullOrEmpty(options.Smtp.Password)
                : !string.IsNullOrEmpty(settings.SmtpPasswordProtected),
            options.Smtp.FromAddress,
            options.Smtp.FromName,
            options.Smtp.TimeoutSeconds,
            settings is not null,
            settings?.UpdatedAt);

    private static EmailVerificationOptions Clone(EmailVerificationOptions source) => new()
    {
        Enabled = source.Enabled,
        PublicBaseUrl = source.PublicBaseUrl,
        TokenLifetimeMinutes = source.TokenLifetimeMinutes,
        ResendCooldownSeconds = source.ResendCooldownSeconds,
        Smtp = new SmtpOptions
        {
            Host = source.Smtp.Host,
            Port = source.Smtp.Port,
            EnableSsl = source.Smtp.EnableSsl,
            UserName = source.Smtp.UserName,
            Password = source.Smtp.Password,
            FromAddress = source.Smtp.FromAddress,
            FromName = source.Smtp.FromName,
            TimeoutSeconds = source.Smtp.TimeoutSeconds
        }
    };
}

internal static class EmailVerificationOptionsValidator
{
    public static void Validate(EmailVerificationOptions options, bool isDevelopment)
    {
        if (options.PublicBaseUrl.Length > 2048 ||
            options.Smtp.Host.Length > 255 ||
            options.Smtp.UserName.Length > 320 ||
            options.Smtp.Password.Length > 1024 ||
            options.Smtp.FromAddress.Length > 254 ||
            options.Smtp.FromName.Length > 128)
        {
            throw new InvalidOperationException("EmailVerification settings exceed their maximum length.");
        }

        if (!options.Enabled)
            return;

        if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var publicBaseUri) ||
            (publicBaseUri.Scheme != Uri.UriSchemeHttp && publicBaseUri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(publicBaseUri.Host) ||
            !string.IsNullOrEmpty(publicBaseUri.UserInfo) ||
            !string.IsNullOrEmpty(publicBaseUri.Query) ||
            !string.IsNullOrEmpty(publicBaseUri.Fragment) ||
            (!isDevelopment && publicBaseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "EmailVerification:PublicBaseUrl must be an absolute HTTP(S) URL without credentials, query, or fragment; HTTPS is required outside Development.");
        }

        if (string.IsNullOrWhiteSpace(options.Smtp.Host) ||
            options.Smtp.Host.Contains("://", StringComparison.Ordinal) ||
            options.Smtp.Port is < 1 or > 65535 ||
            options.Smtp.TimeoutSeconds is < 1 or > 120 ||
            options.TokenLifetimeMinutes is < 5 or > 10080 ||
            options.ResendCooldownSeconds is < 1 or > 3600 ||
            (!isDevelopment && !options.Smtp.EnableSsl))
        {
            throw new InvalidOperationException("EmailVerification SMTP or token settings are invalid.");
        }

        try
        {
            _ = new MailAddress(options.Smtp.FromAddress);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("EmailVerification:Smtp:FromAddress is invalid.", exception);
        }
    }
}

internal sealed class EmailVerificationSecretProtector
{
    private const string Prefix = "v1:";
    private readonly byte[] _key;

    public EmailVerificationSecretProtector(string applicationSecret)
    {
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"NoCTF.EmailVerificationSettings.v1\0{applicationSecret}"));
    }

    public string Protect(string value)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string value)
    {
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Stored SMTP credential has an unsupported format.");
        try
        {
            var payload = Convert.FromBase64String(value[Prefix.Length..]);
            if (payload.Length < 29)
                throw new CryptographicException();
            var nonce = payload.AsSpan(0, 12);
            var tag = payload.AsSpan(12, 16);
            var ciphertext = payload.AsSpan(28);
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(_key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            throw new InvalidOperationException("Stored SMTP credential cannot be decrypted.", exception);
        }
    }
}
