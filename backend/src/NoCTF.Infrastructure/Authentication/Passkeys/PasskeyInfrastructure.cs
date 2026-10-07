using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.Passkeys;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Authentication.Passkeys;

internal static class PasskeyInfrastructure
{
    internal static void AddNoCtfPasskeys(this IServiceCollection services, IConfiguration configuration, bool development)
    {
        services.AddOptions<PasskeyOptions>().Bind(configuration.GetSection("Passkeys"))
            .Validate(value => value.IsValid(development), "Passkeys must use a pinned RP domain and exact HTTPS origins; only local development permits HTTP localhost.").ValidateOnStart();
        // Identity supplies the protocol verifier only; JWTs and account lifecycle stay in NoCTF.
        services.AddIdentityCore<User>().AddUserStore<PasskeyIdentityStore>();
        services.AddOptions<IdentityPasskeyOptions>().Configure<IOptions<PasskeyOptions>>((options, configured) =>
        {
            var value = configured.Value;
            options.ServerDomain = value.ServerDomain;
            options.AuthenticatorTimeout = TimeSpan.FromSeconds(value.CeremonyLifetimeSeconds);
            options.ChallengeSize = 32;
            options.UserVerificationRequirement = "required";
            options.ResidentKeyRequirement = "required";
            options.AttestationConveyancePreference = "none";
            options.IsAllowedAlgorithm = algorithm => algorithm is -7 or -257;
            options.ValidateOrigin = context => ValueTask.FromResult(!context.CrossOrigin && context.TopOrigin is null
                && value.AllowsOrigin(context.Origin) && context.HttpContext.Request.Headers.Origin is [var origin]
                && string.Equals(origin, context.Origin, StringComparison.Ordinal));
        });
        services.AddScoped<IPasskeyHandler<User>, PasskeyHandler<User>>();
        services.AddScoped<IPasskeyProtocol, PasskeyProtocol>();
    }
}
