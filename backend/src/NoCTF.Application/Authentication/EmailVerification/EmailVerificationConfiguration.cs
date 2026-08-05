using System.Net.Mail;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Authentication.EmailVerification;

public sealed record EmailVerificationConfigurationView(
    bool Enabled,
    string PublicBaseUrl,
    int TokenLifetimeMinutes,
    int ResendCooldownSeconds,
    string SmtpHost,
    int SmtpPort,
    SmtpSecurityMode SmtpSecurityMode,
    string SmtpUserName,
    bool SmtpPasswordConfigured,
    string SmtpFromAddress,
    string SmtpFromName,
    int SmtpTimeoutSeconds,
    long Revision,
    DateTimeOffset UpdatedAt);

public sealed record UpdateEmailVerificationConfigurationCommand(
    bool Enabled,
    string PublicBaseUrl,
    int TokenLifetimeMinutes,
    int ResendCooldownSeconds,
    string SmtpHost,
    int SmtpPort,
    SmtpSecurityMode SmtpSecurityMode,
    string SmtpUserName,
    string SmtpFromAddress,
    string SmtpFromName,
    int SmtpTimeoutSeconds,
    long ExpectedRevision,
    DateTimeOffset Now);

public enum EmailVerificationConfigurationUpdateState
{
    Updated,
    RevisionConflict,
    Invalid
}

public enum EmailVerificationConfigurationError
{
    PublicBaseUrlInvalid,
    TokenLifetimeInvalid,
    ResendCooldownInvalid,
    SmtpHostRequired,
    SmtpHostInvalid,
    SmtpPortInvalid,
    SmtpSecurityModeInvalid,
    SmtpUserNameInvalid,
    SmtpPasswordRequired,
    SmtpFromAddressRequired,
    SmtpFromAddressInvalid,
    SmtpFromNameRequired,
    SmtpFromNameInvalid,
    SmtpTimeoutInvalid,
    SmtpPasswordInvalid
}

public sealed record EmailVerificationConfigurationMutationResult(
    EmailVerificationConfigurationUpdateState State,
    EmailVerificationConfigurationView? Configuration = null,
    IReadOnlyList<EmailVerificationConfigurationError>? ValidationErrors = null)
{
    public IReadOnlyList<EmailVerificationConfigurationError> Errors => ValidationErrors ?? [];
}

public interface IEmailVerificationConfigurationStore
{
    Task<EmailVerificationConfigurationView> GetAsync(CancellationToken cancellationToken);
    Task<EmailVerificationConfigurationView?> UpdateAsync(
        UpdateEmailVerificationConfigurationCommand command,
        CancellationToken cancellationToken);
    Task<EmailVerificationConfigurationView?> ReplacePasswordAsync(
        long expectedRevision,
        string password,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class ManageEmailVerificationConfiguration(
    IEmailVerificationConfigurationStore store)
{
    public Task<EmailVerificationConfigurationView> GetAsync(
        CancellationToken ct = default) =>
        store.GetAsync(ct);

    public async Task<EmailVerificationConfigurationMutationResult> UpdateAsync(
        UpdateEmailVerificationConfigurationCommand command,
        CancellationToken ct = default)
    {
        var current = await store.GetAsync(ct);
        var errors = Validate(command, current.SmtpPasswordConfigured);
        if (errors.Count > 0)
        {
            return new(
                EmailVerificationConfigurationUpdateState.Invalid,
                ValidationErrors: errors);
        }

        var updated = await store.UpdateAsync(command, ct);
        return updated is null
            ? new(EmailVerificationConfigurationUpdateState.RevisionConflict)
            : new(EmailVerificationConfigurationUpdateState.Updated, updated);
    }

    public async Task<EmailVerificationConfigurationMutationResult> ReplacePasswordAsync(
        long expectedRevision,
        string password,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length > 1024)
        {
            return new(
                EmailVerificationConfigurationUpdateState.Invalid,
                ValidationErrors: [EmailVerificationConfigurationError.SmtpPasswordInvalid]);
        }

        var updated = await store.ReplacePasswordAsync(expectedRevision, password, now, ct);
        return updated is null
            ? new(EmailVerificationConfigurationUpdateState.RevisionConflict)
            : new(EmailVerificationConfigurationUpdateState.Updated, updated);
    }

    private static IReadOnlyList<EmailVerificationConfigurationError> Validate(
        UpdateEmailVerificationConfigurationCommand command,
        bool passwordConfigured)
    {
        var errors = new List<EmailVerificationConfigurationError>();
        if (!Uri.TryCreate(command.PublicBaseUrl, UriKind.Absolute, out var publicBaseUrl)
            || !string.Equals(publicBaseUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(publicBaseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(publicBaseUrl.Query)
            || !string.IsNullOrEmpty(publicBaseUrl.Fragment)
            || !string.IsNullOrEmpty(publicBaseUrl.UserInfo)
            || command.PublicBaseUrl.Length > 2048)
        {
            errors.Add(EmailVerificationConfigurationError.PublicBaseUrlInvalid);
        }
        if (command.TokenLifetimeMinutes is < 5 or > 10_080)
            errors.Add(EmailVerificationConfigurationError.TokenLifetimeInvalid);
        if (command.ResendCooldownSeconds is < 30 or > 3600)
            errors.Add(EmailVerificationConfigurationError.ResendCooldownInvalid);
        if (command.SmtpPort is < 1 or > 65_535)
            errors.Add(EmailVerificationConfigurationError.SmtpPortInvalid);
        if (!Enum.IsDefined(command.SmtpSecurityMode))
            errors.Add(EmailVerificationConfigurationError.SmtpSecurityModeInvalid);
        if (command.SmtpTimeoutSeconds is < 1 or > 120)
            errors.Add(EmailVerificationConfigurationError.SmtpTimeoutInvalid);
        if (command.SmtpHost.Length > 253)
            errors.Add(EmailVerificationConfigurationError.SmtpHostInvalid);
        if (command.SmtpUserName.Length > 320)
            errors.Add(EmailVerificationConfigurationError.SmtpUserNameInvalid);
        if (command.SmtpFromName.Length > 100)
            errors.Add(EmailVerificationConfigurationError.SmtpFromNameInvalid);
        if (command.SmtpFromAddress.Length > 320
            || !string.IsNullOrWhiteSpace(command.SmtpFromAddress)
                && !MailAddress.TryCreate(command.SmtpFromAddress, out _))
        {
            errors.Add(EmailVerificationConfigurationError.SmtpFromAddressInvalid);
        }

        if (!command.Enabled)
            return errors;

        if (string.IsNullOrWhiteSpace(command.SmtpHost))
            errors.Add(EmailVerificationConfigurationError.SmtpHostRequired);
        if (!string.IsNullOrWhiteSpace(command.SmtpUserName) && !passwordConfigured)
            errors.Add(EmailVerificationConfigurationError.SmtpPasswordRequired);
        if (string.IsNullOrWhiteSpace(command.SmtpFromAddress))
            errors.Add(EmailVerificationConfigurationError.SmtpFromAddressRequired);
        if (string.IsNullOrWhiteSpace(command.SmtpFromName))
            errors.Add(EmailVerificationConfigurationError.SmtpFromNameRequired);
        return errors;
    }
}

public sealed record EmailVerificationDeliveryConfiguration(
    bool Enabled,
    string PublicBaseUrl,
    string SmtpHost,
    int SmtpPort,
    SmtpSecurityMode SmtpSecurityMode,
    string SmtpUserName,
    string? SmtpPassword,
    string SmtpFromAddress,
    string SmtpFromName,
    int SmtpTimeoutSeconds);

public interface IEmailVerificationDeliveryConfigurationReader
{
    Task<EmailVerificationDeliveryConfiguration?> GetDeliveryConfigurationAsync(
        bool requireEnabled,
        CancellationToken cancellationToken);
}

public enum EmailVerificationDeliveryState
{
    Sent,
    Disabled,
    NotConfigured,
    RecipientNotFound
}

public enum EmailVerificationDeliveryFailure
{
    ConnectionFailed,
    AuthenticationFailed,
    MessageRejected,
    TimedOut,
    TransportFailed
}

public sealed class EmailVerificationDeliveryException(
    EmailVerificationDeliveryFailure failure)
    : Exception("Email verification delivery failed.")
{
    public EmailVerificationDeliveryFailure Failure { get; } = failure;
}

public interface IEmailVerificationDelivery
{
    Task<EmailVerificationDeliveryState> SendVerificationAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken);
    Task<EmailVerificationDeliveryState> SendTestAsync(
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed class SendEmailVerificationTest(IEmailVerificationDelivery delivery)
{
    public Task<EmailVerificationDeliveryState> ExecuteAsync(
        Guid userId,
        CancellationToken ct = default) =>
        delivery.SendTestAsync(userId, ct);
}
