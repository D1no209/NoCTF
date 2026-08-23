using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Identity;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.Platform;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<SmtpSecurityModeProtocol>))]
public enum SmtpSecurityModeProtocol
{
    None,
    SslOnConnect,
    StartTls
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<EmailVerificationConfigurationFailureCode>))]
public enum EmailVerificationConfigurationFailureCode
{
    Invalid
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<PlatformProblemCode>))]
public enum PlatformProblemCode
{
    SmtpPasswordInvalid,
    EmailDeliveryNotConfigured,
    SmtpDeliveryFailed
}

[Mapper]
internal static partial class SmtpSecurityModeProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial SmtpSecurityMode ToDomain(SmtpSecurityModeProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial SmtpSecurityModeProtocol ToProtocol(SmtpSecurityMode value);
}

public sealed class UpdateEmailVerificationConfigurationRequest
{
    public required bool Enabled { get; set; }
    public required string PublicBaseUrl { get; set; }
    public required int TokenLifetimeMinutes { get; set; }
    public required int ResendCooldownSeconds { get; set; }
    public required int PasswordResetTokenLifetimeMinutes { get; set; }
    public required int PasswordResetCooldownSeconds { get; set; }
    public required int PasswordResetMaxRequestsPerHour { get; set; }
    public required string SmtpHost { get; set; }
    public required int SmtpPort { get; set; }
    public required SmtpSecurityModeProtocol SmtpSecurityMode { get; set; }
    public required string SmtpUserName { get; set; }
    public required string SmtpFromAddress { get; set; }
    public required string SmtpFromName { get; set; }
    public required int SmtpTimeoutSeconds { get; set; }
}

public sealed class UpdateEmailVerificationConfigurationValidator
    : Validator<UpdateEmailVerificationConfigurationRequest>
{
    public UpdateEmailVerificationConfigurationValidator()
    {
        RuleFor(request => request.PublicBaseUrl).NotEmpty().MaximumLength(2048);
        RuleFor(request => request.TokenLifetimeMinutes).InclusiveBetween(5, 10_080);
        RuleFor(request => request.ResendCooldownSeconds).InclusiveBetween(30, 3600);
        RuleFor(request => request.PasswordResetTokenLifetimeMinutes).InclusiveBetween(5, 1440);
        RuleFor(request => request.PasswordResetCooldownSeconds).InclusiveBetween(30, 3600);
        RuleFor(request => request.PasswordResetMaxRequestsPerHour).InclusiveBetween(1, 24);
        RuleFor(request => request.SmtpHost).MaximumLength(253);
        RuleFor(request => request.SmtpPort).InclusiveBetween(1, 65_535);
        RuleFor(request => request.SmtpUserName).MaximumLength(320);
        RuleFor(request => request.SmtpFromAddress).MaximumLength(320);
        RuleFor(request => request.SmtpFromName).MaximumLength(100);
        RuleFor(request => request.SmtpTimeoutSeconds).InclusiveBetween(1, 120);
    }
}

public sealed class UpdateEmailVerificationConfigurationEndpoint(
    ManageEmailVerificationConfiguration configuration)
    : Endpoint<UpdateEmailVerificationConfigurationRequest,
        Results<Ok<EmailVerificationConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/platform/email-verification/configuration");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformUpdateEmailVerificationConfiguration"));
        Summary(summary =>
        {
            summary.Summary = "Updates email verification configuration.";
            summary.Description =
                "Applies non-secret verification and SMTP settings.";
        });
    }

    public override async Task<
        Results<Ok<EmailVerificationConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        UpdateEmailVerificationConfigurationRequest request,
        CancellationToken ct)
    {
        var result = await configuration.UpdateAsync(new(
            request.Enabled,
            request.PublicBaseUrl,
            request.TokenLifetimeMinutes,
            request.ResendCooldownSeconds,
            request.PasswordResetTokenLifetimeMinutes,
            request.PasswordResetCooldownSeconds,
            request.PasswordResetMaxRequestsPerHour,
            request.SmtpHost,
            request.SmtpPort,
            SmtpSecurityModeProtocolMapper.ToDomain(request.SmtpSecurityMode),
            request.SmtpUserName,
            request.SmtpFromAddress,
            request.SmtpFromName,
            request.SmtpTimeoutSeconds,
            DateTimeOffset.UtcNow), ct);
        if (result.State == EmailVerificationConfigurationUpdateState.Invalid)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Email verification configuration is invalid.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = EmailVerificationConfigurationFailureCode.Invalid,
                    ["errors"] = result.Errors.Select(error => error.ToString()).ToArray()
                });
        }
        return TypedResults.Ok(EmailVerificationConfigurationMapping.ToResponse(
            result.Configuration!));
    }
}
