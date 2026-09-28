using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Endpoints.Platform;
using NoCTF.API.Serialization;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Admission;
using NoCTF.Application.Common;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Platform;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Platform;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<SmtpSecurityModeProtocol>))]
public enum SmtpSecurityModeProtocol
{
    None,
    SslOnConnect,
    StartTls
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<PlatformProblemCode>))]
public enum PlatformProblemCode
{
    HumanVerificationSecretInvalid,
    CapConfigurationInvalid,
    CapProviderUnavailable,
    SmtpPasswordInvalid,
    EmailVerificationDisabled,
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

public sealed class PlatformBrandingPatchRequest
{
    public required string Name { get; set; }
    public required string? Description { get; set; }
}

public sealed class PlatformEmailVerificationPatchRequest
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

public sealed class PlatformHumanVerificationPatchRequest
{
    public required bool Enabled { get; set; }
    public required bool RuntimeEnabled { get; set; }
    public required bool EvaluationEnabled { get; set; }
    public required HumanVerificationProviderProtocol Provider { get; set; }
    public required string CapServerUrl { get; set; }
    public required string CapSiteKey { get; set; }
    public required string TurnstileSiteKey { get; set; }
    public required string[] TurnstileAllowedHostnames { get; set; }
}

public sealed class PlatformExperimentalFeaturesPatchRequest
{
    public required bool CtfPatchVerificationEnabled { get; set; }
}

public sealed class PatchPlatformConfigurationRequest
{
    public PlatformBrandingPatchRequest? Branding { get; set; }
    public PlatformHumanVerificationPatchRequest? HumanVerification { get; set; }
    public PlatformEmailVerificationPatchRequest? EmailVerification { get; set; }
    public PlatformExperimentalFeaturesPatchRequest? ExperimentalFeatures { get; set; }
}

[Flags]
internal enum PlatformConfigurationPatchSection
{
    None = 0,
    Branding = 1 << 0,
    HumanVerification = 1 << 1,
    EmailVerification = 1 << 2,
    ExperimentalFeatures = 1 << 3
}

public sealed class PatchPlatformConfigurationValidator
    : Validator<PatchPlatformConfigurationRequest>
{
    public PatchPlatformConfigurationValidator()
    {
        RuleFor(request => request).Must(request => request.Branding is not null
            || request.HumanVerification is not null
            || request.EmailVerification is not null
            || request.ExperimentalFeatures is not null)
            .WithMessage("At least one platform configuration section is required.");
        RuleFor(request => request.Branding!.Name).NotEmpty()
            .MaximumLength(PlatformConfigurationRules.MaximumNameLength)
            .When(request => request.Branding is not null);
        RuleFor(request => request.Branding!.Description)
            .MaximumLength(PlatformConfigurationRules.MaximumDescriptionLength)
            .When(request => request.Branding is not null);
        RuleFor(request => request.HumanVerification!.Provider).IsInEnum()
            .When(request => request.HumanVerification is not null);
        RuleFor(request => request.HumanVerification!.CapServerUrl)
            .NotNull()
            .MaximumLength(HumanVerificationConfigurationRules.MaximumUrlLength)
            .When(request => request.HumanVerification is not null);
        RuleFor(request => request.HumanVerification!.CapSiteKey)
            .NotNull()
            .MaximumLength(HumanVerificationConfigurationRules.MaximumSiteKeyLength)
            .When(request => request.HumanVerification is not null);
        RuleFor(request => request.HumanVerification!.TurnstileSiteKey)
            .NotNull()
            .MaximumLength(HumanVerificationConfigurationRules.MaximumSiteKeyLength)
            .When(request => request.HumanVerification is not null);
        RuleFor(request => request.HumanVerification!.TurnstileAllowedHostnames)
            .NotNull()
            .Must(hostnames => hostnames is
                { Length: <= HumanVerificationConfigurationRules.MaximumAllowedHostnames })
            .When(request => request.HumanVerification is not null);
        RuleForEach(request => request.HumanVerification!.TurnstileAllowedHostnames)
            .NotEmpty().MaximumLength(253)
            .When(request => request.HumanVerification is not null);
        RuleFor(request => request.EmailVerification!.PublicBaseUrl).NotEmpty().MaximumLength(2048)
            .When(request => request.EmailVerification is not null);
        RuleFor(request => request.EmailVerification!.SmtpPort).InclusiveBetween(1, 65_535)
            .When(request => request.EmailVerification is not null);
        RuleFor(request => request.EmailVerification!.SmtpSecurityMode).IsInEnum()
            .When(request => request.EmailVerification is not null);
    }
}

[Mapper(
    AutoUserMappings = false,
    RequiredMappingStrategy = RequiredMappingStrategy.Both,
    UseDeepCloning = true)]
public static partial class PlatformSettingsPatchMapper
{
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.CtfPatchVerificationEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationRuntimeEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationEvaluationEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationProvider))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapServerUrl))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapSiteKey))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapSecretCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileSiteKey))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileSecretCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileAllowedHostnames))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileHostnames))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailVerificationEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailPublicBaseUrl))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailVerificationTokenLifetimeMinutes))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailVerificationResendCooldownSeconds))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailPasswordResetTokenLifetimeMinutes))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailPasswordResetCooldownSeconds))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailPasswordResetMaxRequestsPerHour))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailSmtpHost))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailSmtpPort))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailSmtpSecurityMode))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailSmtpUserName))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailSmtpFromAddress))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailSmtpFromName))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailSmtpTimeoutSeconds))]
    [MapperIgnoreTarget(nameof(PlatformSettings.Id))]
    [MapperIgnoreTarget(nameof(PlatformSettings.ConcurrencyStamp))]
    [MapperIgnoreTarget(nameof(PlatformSettings.LogoFileId))]
    [MapperIgnoreTarget(nameof(PlatformSettings.LogoFile))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailSmtpPasswordCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.UpdatedAt))]
    [MapperIgnoreTarget(nameof(PlatformSettings.SsoConfiguration))]
    [MapperIgnoreTarget(nameof(PlatformSettings.SsoEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.SsoPublicBaseUrl))]
    [MapperIgnoreTarget(nameof(PlatformSettings.SsoProviders))]
    public static partial void ApplyBrandingAsAdministrator(
        PlatformBrandingPatchRequest request,
        [MappingTarget] PlatformSettings target);

    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.Enabled), nameof(PlatformSettings.EmailVerificationEnabled))]
    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.PublicBaseUrl), nameof(PlatformSettings.EmailPublicBaseUrl))]
    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.TokenLifetimeMinutes), nameof(PlatformSettings.EmailVerificationTokenLifetimeMinutes))]
    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.ResendCooldownSeconds), nameof(PlatformSettings.EmailVerificationResendCooldownSeconds))]
    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.PasswordResetTokenLifetimeMinutes), nameof(PlatformSettings.EmailPasswordResetTokenLifetimeMinutes))]
    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.PasswordResetCooldownSeconds), nameof(PlatformSettings.EmailPasswordResetCooldownSeconds))]
    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.PasswordResetMaxRequestsPerHour), nameof(PlatformSettings.EmailPasswordResetMaxRequestsPerHour))]
    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.SmtpHost), nameof(PlatformSettings.EmailSmtpHost))]
    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.SmtpPort), nameof(PlatformSettings.EmailSmtpPort))]
    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.SmtpSecurityMode), nameof(PlatformSettings.EmailSmtpSecurityMode))]
    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.SmtpUserName), nameof(PlatformSettings.EmailSmtpUserName))]
    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.SmtpFromAddress), nameof(PlatformSettings.EmailSmtpFromAddress))]
    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.SmtpFromName), nameof(PlatformSettings.EmailSmtpFromName))]
    [MapProperty(nameof(PlatformEmailVerificationPatchRequest.SmtpTimeoutSeconds), nameof(PlatformSettings.EmailSmtpTimeoutSeconds))]
    [MapperIgnoreTarget(nameof(PlatformSettings.Name))]
    [MapperIgnoreTarget(nameof(PlatformSettings.Description))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.CtfPatchVerificationEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationRuntimeEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationEvaluationEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationProvider))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapServerUrl))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapSiteKey))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapSecretCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileSiteKey))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileSecretCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileAllowedHostnames))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileHostnames))]
    [MapperIgnoreTarget(nameof(PlatformSettings.Id))]
    [MapperIgnoreTarget(nameof(PlatformSettings.ConcurrencyStamp))]
    [MapperIgnoreTarget(nameof(PlatformSettings.LogoFileId))]
    [MapperIgnoreTarget(nameof(PlatformSettings.LogoFile))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailSmtpPasswordCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.UpdatedAt))]
    [MapperIgnoreTarget(nameof(PlatformSettings.SsoConfiguration))]
    [MapperIgnoreTarget(nameof(PlatformSettings.SsoEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.SsoPublicBaseUrl))]
    [MapperIgnoreTarget(nameof(PlatformSettings.SsoProviders))]
    public static partial void ApplyEmailAsAdministrator(
        PlatformEmailVerificationPatchRequest request,
        [MappingTarget] PlatformSettings target);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial SmtpSecurityMode ToDomain(SmtpSecurityModeProtocol value);
}

public sealed class PatchPlatformConfigurationEndpoint(
    ManagePlatformConfiguration configuration,
    ManageHumanVerificationConfiguration humanVerification,
    ManageEmailVerificationConfiguration emailVerification,
    IAtomicAggregatePatch atomicPatch,
    LinkGenerator links,
    TimeProvider timeProvider)
    : Endpoint<PatchPlatformConfigurationRequest,
        Results<Ok<AdminPlatformConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Patch("/admin/platform/configuration");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformPatchConfiguration"));
        Summary(summary => summary.Summary = "Updates selected platform configuration sections.");
    }

    public override async Task<Results<Ok<AdminPlatformConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        PatchPlatformConfigurationRequest request,
        CancellationToken ct)
    {
        var branding = await configuration.GetAsync(ct);
        var email = await emailVerification.GetAsync(ct);
        var sections = ResolveSections(request);
        var target = new PlatformSettings
        {
            Id = 1,
            Name = branding.Name,
            Description = branding.Description,
            LogoFileId = branding.LogoFileId,
            EmailVerificationEnabled = email.Enabled,
            EmailPublicBaseUrl = email.PublicBaseUrl,
            EmailVerificationTokenLifetimeMinutes = email.TokenLifetimeMinutes,
            EmailVerificationResendCooldownSeconds = email.ResendCooldownSeconds,
            EmailPasswordResetTokenLifetimeMinutes = email.PasswordResetTokenLifetimeMinutes,
            EmailPasswordResetCooldownSeconds = email.PasswordResetCooldownSeconds,
            EmailPasswordResetMaxRequestsPerHour = email.PasswordResetMaxRequestsPerHour,
            EmailSmtpHost = email.SmtpHost,
            EmailSmtpPort = email.SmtpPort,
            EmailSmtpSecurityMode = email.SmtpSecurityMode,
            EmailSmtpUserName = email.SmtpUserName,
            EmailSmtpFromAddress = email.SmtpFromAddress,
            EmailSmtpFromName = email.SmtpFromName,
            EmailSmtpTimeoutSeconds = email.SmtpTimeoutSeconds,
            CtfPatchVerificationEnabled = branding.CtfPatchVerificationEnabled,
            UpdatedAt = branding.UpdatedAt
        };
        if ((sections & PlatformConfigurationPatchSection.Branding) != 0)
            PlatformSettingsPatchMapper.ApplyBrandingAsAdministrator(request.Branding!, target);
        if ((sections & PlatformConfigurationPatchSection.EmailVerification) != 0)
            PlatformSettingsPatchMapper.ApplyEmailAsAdministrator(
                request.EmailVerification!, target);
        return await atomicPatch.ExecuteAsync(ApplyAsync, ct);

        async Task<AtomicAggregatePatchDecision<Results<Ok<AdminPlatformConfigurationResponse>, ProblemHttpResult>>> ApplyAsync(
            CancellationToken transactionCt)
        {
            if ((sections & PlatformConfigurationPatchSection.Branding) != 0)
            {
                var result = await configuration.UpdateAsync(
                    target.Name,
                    target.Description,
                    timeProvider.GetUtcNow(),
                    transactionCt);
                if (result.State != PlatformConfigurationUpdateState.Updated)
                {
                    return AtomicAggregatePatchDecision<Results<
                        Ok<AdminPlatformConfigurationResponse>,
                        ProblemHttpResult>>
                        .Rollback(Invalid("Platform branding is invalid."));
                }
            }
            if ((sections & PlatformConfigurationPatchSection.HumanVerification) != 0)
            {
                var humanRequest = request.HumanVerification!;
                var result = await humanVerification.UpdateAsync(new(
                    humanRequest.Enabled,
                    PublicPlatformConfigurationMapping.ToDomain(humanRequest.Provider),
                    humanRequest.CapServerUrl,
                    humanRequest.CapSiteKey,
                    humanRequest.TurnstileSiteKey,
                    humanRequest.TurnstileAllowedHostnames,
                    timeProvider.GetUtcNow(),
                    humanRequest.RuntimeEnabled,
                    humanRequest.EvaluationEnabled),
                    transactionCt);
                if (result.State != HumanVerificationConfigurationUpdateState.Updated)
                {
                    return AtomicAggregatePatchDecision<Results<
                        Ok<AdminPlatformConfigurationResponse>,
                        ProblemHttpResult>>
                        .Rollback(HumanVerificationInvalid(result.Errors));
                }
            }
            if ((sections & PlatformConfigurationPatchSection.EmailVerification) != 0)
            {
                var result = await emailVerification.UpdateAsync(new(
                    target.EmailVerificationEnabled,
                    target.EmailPublicBaseUrl,
                    target.EmailVerificationTokenLifetimeMinutes,
                    target.EmailVerificationResendCooldownSeconds,
                    target.EmailPasswordResetTokenLifetimeMinutes,
                    target.EmailPasswordResetCooldownSeconds,
                    target.EmailPasswordResetMaxRequestsPerHour,
                    target.EmailSmtpHost,
                    target.EmailSmtpPort,
                    target.EmailSmtpSecurityMode!.Value,
                    target.EmailSmtpUserName,
                    target.EmailSmtpFromAddress,
                    target.EmailSmtpFromName,
                    target.EmailSmtpTimeoutSeconds,
                    timeProvider.GetUtcNow()), transactionCt);
                if (result.State != EmailVerificationConfigurationUpdateState.Updated)
                {
                    return AtomicAggregatePatchDecision<Results<
                        Ok<AdminPlatformConfigurationResponse>,
                        ProblemHttpResult>>
                        .Rollback(Invalid(string.Join(" ", result.Errors)));
                }
            }
            if ((sections & PlatformConfigurationPatchSection.ExperimentalFeatures) != 0)
            {
                await configuration.UpdateExperimentalFeaturesAsync(
                    request.ExperimentalFeatures!.CtfPatchVerificationEnabled,
                    timeProvider.GetUtcNow(),
                    transactionCt);
            }

            var response = await LoadResponseAsync(transactionCt);
            Results<Ok<AdminPlatformConfigurationResponse>, ProblemHttpResult> outcome =
                TypedResults.Ok(response);
            return AtomicAggregatePatchDecision<Results<Ok<AdminPlatformConfigurationResponse>,
                ProblemHttpResult>>.Commit(outcome);
        }
    }

    private async Task<AdminPlatformConfigurationResponse> LoadResponseAsync(CancellationToken ct)
    {
        var current = await configuration.GetAsync(ct);
        var verification = await humanVerification.GetAsync(ct);
        return new(
            PlatformConfigurationMapping.ToResponse(current, links, HttpContext),
            AdminHumanVerificationConfigurationMapping.ToResponse(verification),
            EmailVerificationConfigurationMapping.ToResponse(
                await emailVerification.GetAsync(ct)),
            new(current.CtfPatchVerificationEnabled));
    }

    private static PlatformConfigurationPatchSection ResolveSections(
        PatchPlatformConfigurationRequest request) =>
        (request.Branding is null ? PlatformConfigurationPatchSection.None
            : PlatformConfigurationPatchSection.Branding)
        | (request.HumanVerification is null ? PlatformConfigurationPatchSection.None
            : PlatformConfigurationPatchSection.HumanVerification)
        | (request.EmailVerification is null ? PlatformConfigurationPatchSection.None
            : PlatformConfigurationPatchSection.EmailVerification)
        | (request.ExperimentalFeatures is null ? PlatformConfigurationPatchSection.None
            : PlatformConfigurationPatchSection.ExperimentalFeatures);

    private static ProblemHttpResult Invalid(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Platform configuration is invalid.",
        detail: detail);

    private static ProblemHttpResult HumanVerificationInvalid(
        IReadOnlyList<HumanVerificationConfigurationError> errors)
    {
        var unavailable = errors.Contains(
            HumanVerificationConfigurationError.CapProviderUnavailable);
        var code = unavailable
            ? PlatformProblemCode.CapProviderUnavailable
            : errors.Contains(HumanVerificationConfigurationError.CapConfigurationInvalid)
                ? PlatformProblemCode.CapConfigurationInvalid
                : (PlatformProblemCode?)null;
        return TypedResults.Problem(
            statusCode: unavailable
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status400BadRequest,
            title: unavailable
                ? "Cap is unavailable."
                : "Platform configuration is invalid.",
            detail: string.Join(" ", errors),
            extensions: code is null
                ? null
                : new Dictionary<string, object?> { ["code"] = code.Value });
    }
}
