using System.Text.Json.Serialization;
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
    string SmtpHost,
    int SmtpPort,
    [property: JsonConverter(typeof(SmtpSecurityModeJsonConverter))]
    SmtpSecurityMode SmtpSecurityMode,
    string SmtpUserName,
    bool SmtpPasswordConfigured,
    string SmtpFromAddress,
    string SmtpFromName,
    int SmtpTimeoutSeconds,
    long Revision,
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
            configuration.SmtpHost,
            configuration.SmtpPort,
            configuration.SmtpSecurityMode,
            configuration.SmtpUserName,
            configuration.SmtpPasswordConfigured,
            configuration.SmtpFromAddress,
            configuration.SmtpFromName,
            configuration.SmtpTimeoutSeconds,
            configuration.Revision,
            configuration.UpdatedAt);
}

public sealed class GetEmailVerificationConfigurationEndpoint(
    ManageEmailVerificationConfiguration configuration)
    : EndpointWithoutRequest<Ok<EmailVerificationConfigurationResponse>>
{
    public override void Configure()
    {
        Get("/admin/platform/email-verification/configuration");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformGetEmailVerificationConfiguration"));
        Summary(summary =>
        {
            summary.Summary = "Gets email verification and SMTP configuration.";
            summary.Description =
                "Returns non-secret platform settings and whether an SMTP password is configured.";
        });
    }

    public override async Task<Ok<EmailVerificationConfigurationResponse>> ExecuteAsync(
        CancellationToken ct) =>
        TypedResults.Ok(EmailVerificationConfigurationMapping.ToResponse(
            await configuration.GetAsync(ct)));
}
