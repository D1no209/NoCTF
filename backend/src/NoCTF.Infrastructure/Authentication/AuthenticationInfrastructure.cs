using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
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
        this IServiceCollection services)
    {
        services.AddSingleton<IRunnerScoringTokenIssuer, RunnerScoringTokenIssuer>();
        services.AddScoped<IUserAuthenticationStore, AuthenticationStore>();
        services.Configure<PasswordHasherOptions>(options =>
            options.IterationCount = 210_000);
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<AdministratorBootstrapper>();
        services.AddScoped<RegisterUser>();
        services.AddScoped<GetCurrentUser>();
        services.AddScoped<GetPublicUserProfile>();
        services.AddScoped<UpdateCurrentUserProfile>();
        services.AddSingleton<IAvatarImageProcessor, SkiaAvatarImageProcessor>();
        services.AddScoped<ReplaceCurrentUserAvatar>();
        services.AddScoped<GetUserAvatar>();
        services.AddScoped<ChangePassword>();
        services.AddScoped<LogoutAll>();
        services.AddScoped<IEmailVerificationStore, EmailVerificationStore>();
        services.AddSingleton<EmailVerificationSecretProtector>();
        services.AddScoped<EmailVerificationConfigurationStore>();
        services.AddScoped<IEmailVerificationConfigurationStore>(provider =>
            provider.GetRequiredService<EmailVerificationConfigurationStore>());
        services.AddScoped<IEmailVerificationDeliveryConfigurationReader>(provider =>
            provider.GetRequiredService<EmailVerificationConfigurationStore>());
        services.AddSingleton<
            IEmailVerificationSmtpClientFactory,
            EmailVerificationSmtpClientFactory>();
        services.AddScoped<SmtpEmailVerificationDelivery>();
        services.AddScoped<IEmailVerificationDelivery>(provider =>
            provider.GetRequiredService<SmtpEmailVerificationDelivery>());
        services.AddScoped<IPasswordResetEmailDelivery>(provider =>
            provider.GetRequiredService<SmtpEmailVerificationDelivery>());
        services.AddScoped<IPasswordResetStore, PasswordResetStore>();
        services.AddScoped<RequestPasswordReset>();
        services.AddScoped<CompletePasswordReset>();
        services.AddScoped<ManageEmailVerificationConfiguration>();
        services.AddScoped<SendEmailVerificationTest>();
        services.AddScoped<ResendEmailVerification>();
        services.AddScoped<VerifyEmail>();
        services.AddScoped<IAccessTokenVersionReader, AccessTokenVersionReader>();
        services.AddSingleton<IAccessTokenIssuer, JwtIssuer>();
        return services;
    }
}
