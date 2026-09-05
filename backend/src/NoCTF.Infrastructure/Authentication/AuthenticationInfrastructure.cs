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
        IConfiguration configuration)
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
        services.Configure<PasswordHasherOptions>(options =>
            options.IterationCount = 210_000);
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<AdministratorBootstrapper>();
        services.AddScoped<RegisterUser>();
        services.AddScoped<GetCurrentUser>();
        services.AddScoped<GetPublicUserProfile>();
        services.AddScoped<UpdateCurrentUserProfile>();
        services.AddSingleton<IAvatarImageProcessor, ImageSharpAvatarImageProcessor>();
        services.AddScoped<ReplaceCurrentUserAvatar>();
        services.AddScoped<GetUserAvatar>();
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
