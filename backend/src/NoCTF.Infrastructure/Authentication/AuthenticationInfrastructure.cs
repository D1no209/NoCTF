using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Authentication;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Application.Authentication.RefreshSession;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Authentication;

internal static class AuthenticationInfrastructure
{
    internal static IServiceCollection AddNoCtfAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        bool development = false)
    {
        services.AddOptions<AuthenticationTokenOptions>()
            .Bind(configuration.GetSection(AuthenticationTokenOptions.SectionName))
            .Validate(options => System.Text.Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "Authentication:SigningKey must contain at least 32 UTF-8 bytes.")
            .Validate(options => options.AccessTokenMinutes > 0,
                "Authentication:AccessTokenMinutes must be positive.")
            .ValidateOnStart();
        services.AddOptions<RunnerScoringOptions>()
            .Bind(configuration.GetSection(RunnerScoringOptions.SectionName))
            .Validate(options => System.Text.Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "RunnerScoring:SigningKey must contain at least 32 UTF-8 bytes.")
            .Validate(options => options.CallbackBaseUrl is null
                || options.CallbackBaseUrl.Scheme is "http" or "https",
                "RunnerScoring:CallbackBaseUrl must be an absolute HTTP(S) URI.")
            .ValidateOnStart();
        services.AddOptions<SeedAdministratorOptions>()
            .Bind(configuration.GetSection(SeedAdministratorOptions.SectionName))
            .Validate(options => string.IsNullOrWhiteSpace(options.Password)
                    || options.Password.Length is >= 8 and <= 1024,
                "SeedAdmin:Password must contain 8 to 1024 characters when configured.")
            .ValidateOnStart();
        services.AddOptions<EmailVerificationProtectionOptions>()
            .Bind(configuration.GetSection(EmailVerificationProtectionOptions.SectionName))
            .Validate(options => string.IsNullOrWhiteSpace(options.EncryptionKey)
                    || IsValidEncryptionKey(options.EncryptionKey),
                "EmailVerification:EncryptionKey must be a Base64-encoded 32-byte key.")
            .ValidateOnStart();
        services.AddSingleton<IRunnerScoringTokenIssuer, RunnerScoringTokenIssuer>();
        services.AddScoped<IUserAuthenticationStore, AuthenticationStore>();
        services.AddScoped<IUserRegistrationStore, AuthenticationStore>();
        services.AddScoped<ICurrentUserProfilePatchStore, AuthenticationStore>();
        services.AddScoped<NoCTF.Application.Commands.Idempotency.IRequestReplay, NoCTF.Infrastructure.Commands.Idempotency.TransactionalRequestReplay>();
        services.AddSingleton<NoCTF.Application.Admission.IRequestAdmission, NoCTF.Infrastructure.Admission.RedisRequestAdmission>();
        if (development)
            services.AddSingleton<NoCTF.Application.Admission.IRequestAdmission, NoCTF.Infrastructure.Admission.DevelopmentRequestAdmission>();
        services.AddSingleton<NoCTF.Application.Admission.ICredentialWorkAdmission, NoCTF.Infrastructure.Admission.CredentialWorkAdmission>();
        services.AddOptions<NoCTF.Application.Admission.RequestAdmissionOptions>()
            .Bind(configuration.GetSection("RequestAdmission"))
            .Validate(value => value.AuthenticationIpPerMinute > 0 && value.AuthenticationAccountPerMinute > 0
                && value.PasswordConcurrency is >= 1 and <= 128 && value.SensitiveIpPerMinute > 0
                && value.RuntimeCommandPerUserPerMinute > 0 && value.PatchConcurrency is >= 1 and <= 32
                && value.PatchPerUserConcurrency > 0 && value.SubmissionPerUserPerMinute > 0
                && value.SubmissionConcurrency is >= 1 and <= 128 && value.SubmissionPerUserConcurrency > 0,
                "RequestAdmission limits must be positive and concurrency budgets must be within platform bounds.")
            .ValidateOnStart();
        services.AddOptions<NoCTF.Application.Authentication.Privacy.AccountPrivacyOptions>()
            .Bind(configuration.GetSection("AccountPrivacy"))
            .Validate(value => value.IpRetentionDays is >= 1 and <= 365, "AccountPrivacy:IpRetentionDays must be between 1 and 365.")
            .ValidateOnStart();
        services.AddScoped<Privacy.AccountPrivacyStore>();
        services.AddScoped<NoCTF.Application.Authentication.Privacy.IAccountPrivacyStore, Privacy.AccountPrivacyStore>();
        services.AddScoped<NoCTF.Application.Authentication.Privacy.IAccountActivityRecorder, Privacy.AccountPrivacyStore>();
        services.AddScoped<NoCTF.Application.Authentication.Privacy.AccountPrivacy>();
        services.Configure<PasswordHasherOptions>(options =>
            options.IterationCount = 210_000);
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<AdministratorBootstrapper>();
        services.AddScoped<RegisterUser>();
        services.AddScoped<GetCurrentUser>();
        services.AddScoped<GetPublicUserProfile>();
        services.AddScoped<UpdateCurrentUserProfile>();
        services.AddScoped<PatchCurrentUserProfile>();
        services.AddSingleton<IAvatarImageProcessor, ImageSharpAvatarImageProcessor>();
        services.AddScoped<ReplaceCurrentUserAvatar>();
        services.AddScoped<GetUserAvatar>();
        services.AddSingleton<IWallpaperImageProcessor, ImageSharpWallpaperImageProcessor>();
        services.AddScoped<ReplaceCurrentUserWallpaper>();
        services.AddScoped<GetCurrentUserWallpaper>();
        services.AddScoped<UpdateCurrentUserWallpaperPreference>();
        services.AddScoped<ChangePassword>();
        services.AddScoped<LogoutAll>();
        services.AddScoped<IEmailVerificationStore, EmailVerificationStore>();
        services.AddSingleton<EmailVerificationSecretProtector>();
        services.AddScoped<
            IEmailVerificationConfigurationStore,
            EmailVerificationConfigurationStore>();
        services.AddScoped<
            IEmailVerificationDeliveryConfigurationReader,
            EmailVerificationConfigurationStore>();
        services.AddSingleton<
            IEmailVerificationSmtpClientFactory,
            EmailVerificationSmtpClientFactory>();
        services.AddScoped<
            IEmailVerificationDelivery,
            SmtpEmailVerificationDelivery>();
        services.AddScoped<
            IPasswordResetEmailDelivery,
            SmtpEmailVerificationDelivery>();
        services.AddScoped<IPasswordResetStore, PasswordResetStore>();
        services.AddScoped<RequestPasswordReset>();
        services.AddScoped<CompletePasswordReset>();
        services.AddScoped<ManageEmailVerificationConfiguration>();
        services.AddScoped<SendEmailVerificationTest>();
        services.AddScoped<ResendEmailVerification>();
        services.AddScoped<RequestEmailVerification>();
        services.AddScoped<VerifyEmail>();
        services.AddScoped<IAccessTokenVersionReader, AccessTokenVersionReader>();
        services.AddSingleton<IAccessTokenIssuer, JwtIssuer>();
        return services;
    }

    private static bool IsValidEncryptionKey(string value)
    {
        Span<byte> key = stackalloc byte[32];
        return Convert.TryFromBase64String(value, key, out var bytesWritten)
            && bytesWritten == key.Length;
    }

}
