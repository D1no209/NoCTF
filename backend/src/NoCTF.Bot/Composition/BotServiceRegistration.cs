using NoCTF.Bot.Providers.Milky;

namespace NoCTF.Bot.Composition;

public static class BotServiceRegistration
{
    public static IServiceCollection AddNoCtfBot(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services
            .AddMilkyChatProvider(configuration)
            .AddNoCtfBotCore(configuration);
}
