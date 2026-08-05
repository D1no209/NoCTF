using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class SmtpSecurityModeJsonConverter()
    : JsonStringEnumConverter<SmtpSecurityMode>(
        namingPolicy: null,
        allowIntegerValues: false);

public sealed class UpdateEmailVerificationConfigurationRequest
{
    public required bool Enabled { get; set; }
    public required string PublicBaseUrl { get; set; }
    public required int TokenLifetimeMinutes { get; set; }
    public required int ResendCooldownSeconds { get; set; }
    public required string SmtpHost { get; set; }
    public required int SmtpPort { get; set; }
    [JsonConverter(typeof(SmtpSecurityModeJsonConverter))]
    public required SmtpSecurityMode SmtpSecurityMode { get; set; }
    public required string SmtpUserName { get; set; }
    public required string SmtpFromAddress { get; set; }
    public required string SmtpFromName { get; set; }
    public required int SmtpTimeoutSeconds { get; set; }
    public required long ExpectedRevision { get; set; }
}

public sealed class UpdateEmailVerificationConfigurationValidator
    : Validator<UpdateEmailVerificationConfigurationRequest>
{
    public UpdateEmailVerificationConfigurationValidator()
    {
        RuleFor(request => request.PublicBaseUrl).NotEmpty().MaximumLength(2048);
        RuleFor(request => request.TokenLifetimeMinutes).InclusiveBetween(5, 10_080);
        RuleFor(request => request.ResendCooldownSeconds).InclusiveBetween(30, 3600);
        RuleFor(request => request.SmtpHost).MaximumLength(253);
        RuleFor(request => request.SmtpPort).InclusiveBetween(1, 65_535);
        RuleFor(request => request.SmtpUserName).MaximumLength(320);
        RuleFor(request => request.SmtpFromAddress).MaximumLength(320);
        RuleFor(request => request.SmtpFromName).MaximumLength(100);
        RuleFor(request => request.SmtpTimeoutSeconds).InclusiveBetween(1, 120);
        RuleFor(request => request.ExpectedRevision).GreaterThan(0);
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
                "Applies non-secret verification and SMTP settings with a revision fence.";
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
            request.SmtpHost,
            request.SmtpPort,
            request.SmtpSecurityMode,
            request.SmtpUserName,
            request.SmtpFromAddress,
            request.SmtpFromName,
            request.SmtpTimeoutSeconds,
            request.ExpectedRevision,
            DateTimeOffset.UtcNow), ct);
        if (result.State == EmailVerificationConfigurationUpdateState.Invalid)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Email verification configuration is invalid.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "email_verification_configuration_invalid",
                    ["errors"] = result.Errors.Select(error => error.ToString()).ToArray()
                });
        }
        if (result.State == EmailVerificationConfigurationUpdateState.RevisionConflict)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Email verification configuration changed.",
                detail: "Reload the configuration and apply the changes again.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "email_verification_configuration_conflict"
                });
        }

        return TypedResults.Ok(EmailVerificationConfigurationMapping.ToResponse(
            result.Configuration!));
    }
}
