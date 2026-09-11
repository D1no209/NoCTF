using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record EmailVerificationConfigurationResponse(
    bool Enabled,
    string PublicBaseUrl,
    int TokenLifetimeMinutes,
    int ResendCooldownSeconds,
    int PasswordResetTokenLifetimeMinutes,
    int PasswordResetCooldownSeconds,
    int PasswordResetMaxRequestsPerHour,
    string SmtpHost,
    int SmtpPort,
    SmtpSecurityModeProtocol SmtpSecurityMode,
    string SmtpUserName,
    bool SmtpPasswordConfigured,
    string SmtpFromAddress,
    string SmtpFromName,
    int SmtpTimeoutSeconds,
    DateTimeOffset UpdatedAt);

internal static class EmailVerificationConfigurationMapping
{
    internal static EmailVerificationConfigurationResponse ToResponse(
        EmailVerificationConfigurationView configuration) =>
        new(
            configuration.Enabled,
            configuration.PublicBaseUrl,
            configuration.TokenLifetimeMinutes,
            configuration.ResendCooldownSeconds,
            configuration.PasswordResetTokenLifetimeMinutes,
            configuration.PasswordResetCooldownSeconds,
            configuration.PasswordResetMaxRequestsPerHour,
            configuration.SmtpHost,
            configuration.SmtpPort,
            SmtpSecurityModeProtocolMapper.ToProtocol(configuration.SmtpSecurityMode),
            configuration.SmtpUserName,
            configuration.SmtpPasswordConfigured,
            configuration.SmtpFromAddress,
            configuration.SmtpFromName,
            configuration.SmtpTimeoutSeconds,
            configuration.UpdatedAt);
}
