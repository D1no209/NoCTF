using Microsoft.Extensions.Options;
using NoCTF.Bot.Providers;

namespace NoCTF.Bot.Providers.Milky;

public static class MilkyServiceRegistration
{
    public static IServiceCollection AddMilkyChatProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MilkyOptions>, MilkyOptionsValidator>();
        services.AddOptions<MilkyOptions>()
            .Bind(configuration.GetSection(MilkyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient<MilkyClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<MilkyOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("NoCTF.Bot.Milky/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
        services.AddSingleton<IChatProvider, MilkyChatProvider>();
        return services;
    }
}
