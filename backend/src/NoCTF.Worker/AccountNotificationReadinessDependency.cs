using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Hosting.Health;

namespace NoCTF.Worker;

public sealed class AccountNotificationReadinessDependency(
    IEmailVerificationConfigurationStore settings,
    IEmailVerificationDeliveryConfigurationReader delivery) : IReadinessDependency
{
    private AccountNotificationReadinessState state =
        AccountNotificationReadinessState.Unknown;

    public string Name => "account-notification-delivery";
    public bool FailureIsCritical => true;
    public IReadOnlyDictionary<string, object> Describe() =>
        new Dictionary<string, object>
        {
            ["state"] = state.ToString()
        };

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        state = AccountNotificationReadinessState.Unknown;
        var configuration = await settings.GetAsync(cancellationToken);
        if (!configuration.Enabled)
        {
            state = AccountNotificationReadinessState.Disabled;
            return;
        }

        state = AccountNotificationReadinessState.ConfigurationUnavailable;
        var deliveryConfiguration = await delivery.GetDeliveryConfigurationAsync(
            requireEnabled: true,
            cancellationToken);
        if (deliveryConfiguration is null)
        {
            throw new InvalidOperationException(
                "Email verification is enabled without a usable delivery configuration.");
        }
        state = AccountNotificationReadinessState.Ready;
    }
}

public enum AccountNotificationReadinessState
{
    Unknown,
    Disabled,
    Ready,
    ConfigurationUnavailable
}
