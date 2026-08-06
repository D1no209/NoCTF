using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Composition;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Tests.Unit.API;

public sealed class EmailVerificationServiceRegistrationTests
{
    [Test]
    public async Task Runtime_api_uses_the_persistent_email_verification_store()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] =
                    "Host=localhost;Database=noctf;Username=noctf;Password=test",
                ["ConnectionStrings:Redis"] = "localhost:6379"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddNoCtfApi(configuration, includeInfrastructure: true);

        var registration = services.Last(descriptor =>
            descriptor.ServiceType == typeof(IEmailVerificationStore));
        await Assert.That(registration.ImplementationType)
            .IsEqualTo(typeof(EmailVerificationStore));

        await AssertImplementationTypeAsync<IEmailVerificationConfigurationStore,
            EmailVerificationConfigurationStore>(services);
        await AssertImplementationTypeAsync<IEmailVerificationDeliveryConfigurationReader,
            EmailVerificationConfigurationStore>(services);
        await AssertImplementationTypeAsync<IEmailVerificationDelivery,
            SmtpEmailVerificationDelivery>(services);
        await AssertImplementationTypeAsync<IPasswordResetEmailDelivery,
            SmtpEmailVerificationDelivery>(services);
    }

    private static async Task AssertImplementationTypeAsync<TService, TImplementation>(
        IServiceCollection services)
    {
        var registration = services.Last(descriptor =>
            descriptor.ServiceType == typeof(TService));
        await Assert.That(registration.ImplementationFactory).IsNull();
        await Assert.That(registration.ImplementationType)
            .IsEqualTo(typeof(TImplementation));
    }
}
