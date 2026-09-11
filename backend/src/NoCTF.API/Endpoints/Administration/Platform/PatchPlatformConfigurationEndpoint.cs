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
using NoCTF.Application.Runtime.PublicAccess;
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
    public required HumanVerificationProviderProtocol Provider { get; set; }
    public required string CapServerUrl { get; set; }
    public required string CapSiteKey { get; set; }
    public required string TurnstileSiteKey { get; set; }
    public required string[] TurnstileAllowedHostnames { get; set; }
}

public sealed class PlatformGatewayPatchRequest
{
    public required bool Enabled { get; set; }
    public required string ConnectorId { get; set; }
    public required string PublicOrigin { get; set; }
    public required string[] DirectOrigins { get; set; }
    public required string PublicRuntimeHost { get; set; }
    public required string? DirectRuntimeHostOverride { get; set; }
    public required int MaxPublishedPorts { get; set; }
}

public sealed class PatchPlatformConfigurationRequest
{
    public PlatformBrandingPatchRequest? Branding { get; set; }
    public PlatformHumanVerificationPatchRequest? HumanVerification { get; set; }
    public PlatformEmailVerificationPatchRequest? EmailVerification { get; set; }
    public PlatformGatewayPatchRequest? PublicGateway { get; set; }
}

[Flags]
internal enum PlatformConfigurationPatchSection
{
    None = 0,
    Branding = 1 << 0,
    HumanVerification = 1 << 1,
    EmailVerification = 1 << 2,
    PublicGateway = 1 << 3
}

public sealed class PatchPlatformConfigurationValidator
    : Validator<PatchPlatformConfigurationRequest>
{
    public PatchPlatformConfigurationValidator()
    {
        RuleFor(request => request).Must(request => request.Branding is not null
            || request.HumanVerification is not null
            || request.EmailVerification is not null
            || request.PublicGateway is not null)
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
        RuleFor(request => request.PublicGateway!.DirectOrigins).NotNull()
            .Must(origins => origins.Length <= 16)
            .When(request => request.PublicGateway is not null);
        RuleForEach(request => request.PublicGateway!.DirectOrigins).NotEmpty().MaximumLength(2048)
            .When(request => request.PublicGateway is not null);
        RuleFor(request => request.PublicGateway!.MaxPublishedPorts).InclusiveBetween(0, 64)
            .When(request => request.PublicGateway is not null);
    }
}

[Mapper(
    AutoUserMappings = false,
    RequiredMappingStrategy = RequiredMappingStrategy.Both,
    UseDeepCloning = true)]
public static partial class PlatformSettingsPatchMapper
{
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationProvider))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapServerUrl))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapSiteKey))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapSecretCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileSiteKey))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileSecretCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileAllowedHostnames))]
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
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayConnectorId))]
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayOrigin))]
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayDirectOrigins))]
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayRuntimeHost))]
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayDirectHostOverride))]
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayMaxPorts))]
    [MapperIgnoreTarget(nameof(PlatformSettings.Id))]
    [MapperIgnoreTarget(nameof(PlatformSettings.LogoFileId))]
    [MapperIgnoreTarget(nameof(PlatformSettings.LogoFile))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailSmtpPasswordCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.UpdatedAt))]
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
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationProvider))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapServerUrl))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapSiteKey))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapSecretCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileSiteKey))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileSecretCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileAllowedHostnames))]
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayConnectorId))]
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayOrigin))]
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayDirectOrigins))]
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayRuntimeHost))]
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayDirectHostOverride))]
    [MapperIgnoreTarget(nameof(PlatformSettings.PublicGatewayMaxPorts))]
    [MapperIgnoreTarget(nameof(PlatformSettings.Id))]
    [MapperIgnoreTarget(nameof(PlatformSettings.LogoFileId))]
    [MapperIgnoreTarget(nameof(PlatformSettings.LogoFile))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailSmtpPasswordCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.UpdatedAt))]
    public static partial void ApplyEmailAsAdministrator(
        PlatformEmailVerificationPatchRequest request,
        [MappingTarget] PlatformSettings target);

    [MapProperty(nameof(PlatformGatewayPatchRequest.Enabled), nameof(PlatformSettings.PublicGatewayEnabled))]
    [MapProperty(nameof(PlatformGatewayPatchRequest.ConnectorId), nameof(PlatformSettings.PublicGatewayConnectorId))]
    [MapProperty(nameof(PlatformGatewayPatchRequest.PublicOrigin), nameof(PlatformSettings.PublicGatewayOrigin))]
    [MapProperty(nameof(PlatformGatewayPatchRequest.DirectOrigins), nameof(PlatformSettings.PublicGatewayDirectOrigins))]
    [MapProperty(nameof(PlatformGatewayPatchRequest.PublicRuntimeHost), nameof(PlatformSettings.PublicGatewayRuntimeHost))]
    [MapProperty(nameof(PlatformGatewayPatchRequest.DirectRuntimeHostOverride), nameof(PlatformSettings.PublicGatewayDirectHostOverride))]
    [MapProperty(nameof(PlatformGatewayPatchRequest.MaxPublishedPorts), nameof(PlatformSettings.PublicGatewayMaxPorts))]
    [MapperIgnoreTarget(nameof(PlatformSettings.Name))]
    [MapperIgnoreTarget(nameof(PlatformSettings.Description))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationEnabled))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationProvider))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapServerUrl))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapSiteKey))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationCapSecretCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileSiteKey))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileSecretCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.HumanVerificationTurnstileAllowedHostnames))]
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
    [MapperIgnoreTarget(nameof(PlatformSettings.LogoFileId))]
    [MapperIgnoreTarget(nameof(PlatformSettings.LogoFile))]
    [MapperIgnoreTarget(nameof(PlatformSettings.EmailSmtpPasswordCiphertext))]
    [MapperIgnoreTarget(nameof(PlatformSettings.UpdatedAt))]
    public static partial void ApplyGatewayAsAdministrator(
        PlatformGatewayPatchRequest request,
        [MappingTarget] PlatformSettings target);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial SmtpSecurityMode ToDomain(SmtpSecurityModeProtocol value);
}

public sealed class PatchPlatformConfigurationEndpoint(
    ManagePlatformConfiguration configuration,
    ManageHumanVerificationConfiguration humanVerification,
    ManageEmailVerificationConfiguration emailVerification,
    ManagePublicGateway publicGateway,
    IAtomicAggregatePatch atomicPatch,
    LinkGenerator links,
    TimeProvider timeProvider)
    : Endpoint<PatchPlatformConfigurationRequest,
        Results<Ok<AdminPlatformConfigurationResponse>,
            Accepted<AdminPlatformConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Patch("/admin/platform/configuration");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformPatchConfiguration"));
        Summary(summary => summary.Summary = "Updates selected platform configuration sections.");
    }

    public override async Task<Results<Ok<AdminPlatformConfigurationResponse>,
        Accepted<AdminPlatformConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        PatchPlatformConfigurationRequest request,
        CancellationToken ct)
    {
        var branding = await configuration.GetAsync(ct);
        var email = await emailVerification.GetAsync(ct);
        var gateway = await publicGateway.GetAsync(ct);
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
            PublicGatewayEnabled = gateway.Policy.Enabled,
            PublicGatewayConnectorId = gateway.Policy.ConnectorId,
            PublicGatewayOrigin = gateway.Policy.PublicOrigin,
            PublicGatewayDirectOrigins = gateway.Policy.DirectOrigins.ToArray(),
            PublicGatewayRuntimeHost = gateway.Policy.PublicRuntimeHost,
            PublicGatewayDirectHostOverride = gateway.Policy.DirectRuntimeHostOverride,
            PublicGatewayMaxPorts = gateway.Policy.MaxPublishedPorts,
            UpdatedAt = branding.UpdatedAt
        };
        if ((sections & PlatformConfigurationPatchSection.Branding) != 0)
            PlatformSettingsPatchMapper.ApplyBrandingAsAdministrator(request.Branding!, target);
        if ((sections & PlatformConfigurationPatchSection.EmailVerification) != 0)
            PlatformSettingsPatchMapper.ApplyEmailAsAdministrator(
                request.EmailVerification!, target);
        if ((sections & PlatformConfigurationPatchSection.PublicGateway) != 0)
            PlatformSettingsPatchMapper.ApplyGatewayAsAdministrator(request.PublicGateway!, target);

        return await atomicPatch.ExecuteAsync(ApplyAsync, ct);

        async Task<AtomicAggregatePatchDecision<Results<Ok<AdminPlatformConfigurationResponse>,
            Accepted<AdminPlatformConfigurationResponse>, ProblemHttpResult>>> ApplyAsync(
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
                        Accepted<AdminPlatformConfigurationResponse>, ProblemHttpResult>>
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
                    timeProvider.GetUtcNow()),
                    transactionCt);
                if (result.State != HumanVerificationConfigurationUpdateState.Updated)
                {
                    return AtomicAggregatePatchDecision<Results<
                        Ok<AdminPlatformConfigurationResponse>,
                        Accepted<AdminPlatformConfigurationResponse>, ProblemHttpResult>>
                        .Rollback(Invalid(string.Join(" ", result.Errors)));
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
                        Accepted<AdminPlatformConfigurationResponse>, ProblemHttpResult>>
                        .Rollback(Invalid(string.Join(" ", result.Errors)));
                }
            }
            if ((sections & PlatformConfigurationPatchSection.PublicGateway) != 0)
            {
                var result = await publicGateway.SaveAsync(new(
                    target.PublicGatewayEnabled,
                    target.PublicGatewayConnectorId,
                    target.PublicGatewayOrigin,
                    target.PublicGatewayDirectOrigins,
                    target.PublicGatewayRuntimeHost,
                    target.PublicGatewayDirectHostOverride,
                    target.PublicGatewayMaxPorts), timeProvider.GetUtcNow(), transactionCt);
                if (result.Errors.Count > 0)
                {
                    return AtomicAggregatePatchDecision<Results<
                        Ok<AdminPlatformConfigurationResponse>,
                        Accepted<AdminPlatformConfigurationResponse>, ProblemHttpResult>>
                        .Rollback(Invalid(string.Join(" ", result.Errors)));
                }
            }

            var response = await LoadResponseAsync(transactionCt);
            Results<Ok<AdminPlatformConfigurationResponse>,
                Accepted<AdminPlatformConfigurationResponse>, ProblemHttpResult> outcome =
                (sections & PlatformConfigurationPatchSection.PublicGateway) == 0
                    ? TypedResults.Ok(response)
                    : TypedResults.Accepted(response.PublicGatewayStatusUrl, response);
            return AtomicAggregatePatchDecision<Results<Ok<AdminPlatformConfigurationResponse>,
                Accepted<AdminPlatformConfigurationResponse>, ProblemHttpResult>>.Commit(outcome);
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
            PublicGatewayConfigurationMapping.ToResponse(
                await publicGateway.GetAsync(ct)),
            "/api/v1/admin/platform/public-gateway/status");
    }

    private static PlatformConfigurationPatchSection ResolveSections(
        PatchPlatformConfigurationRequest request) =>
        (request.Branding is null ? PlatformConfigurationPatchSection.None
            : PlatformConfigurationPatchSection.Branding)
        | (request.HumanVerification is null ? PlatformConfigurationPatchSection.None
            : PlatformConfigurationPatchSection.HumanVerification)
        | (request.EmailVerification is null ? PlatformConfigurationPatchSection.None
            : PlatformConfigurationPatchSection.EmailVerification)
        | (request.PublicGateway is null ? PlatformConfigurationPatchSection.None
            : PlatformConfigurationPatchSection.PublicGateway);

    private static ProblemHttpResult Invalid(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Platform configuration is invalid.",
        detail: detail);
}
