using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Hosting.Health;

namespace NoCTF.Worker;

public sealed class AccountNotificationReadinessDependency(
    IServiceScopeFactory scopeFactory) : IReadinessDependency
{
    public string Name => "account-notification-delivery";
    public bool FailureIsCritical => true;

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settings = await scope.ServiceProvider
            .GetRequiredService<IEmailVerificationConfigurationStore>()
            .GetAsync(cancellationToken);
        if (!settings.Enabled)
            return;

        var delivery = await scope.ServiceProvider
            .GetRequiredService<IEmailVerificationDeliveryConfigurationReader>()
            .GetDeliveryConfigurationAsync(requireEnabled: true, cancellationToken);
        if (delivery is null)
        {
            throw new InvalidOperationException(
                "Email verification is enabled without a usable delivery configuration.");
        }
    }
}
