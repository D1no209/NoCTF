using Microsoft.Extensions.Options;
using NoCTF.Bot.Broadcasting;
using NoCTF.Bot.Commands;
using NoCTF.Bot.Configuration;
using NoCTF.Bot.Hosting;
using NoCTF.Bot.NoCtf;
using NoCTF.Bot.Persistence;
using NoCTF.Bot.Providers;

namespace NoCTF.Bot.Composition;

public static class BotCoreServiceRegistration
{
    public static IServiceCollection AddNoCtfBotCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<NoCtfBotOptions>, NoCtfBotOptionsValidator>();
        services.AddSingleton<IValidateOptions<RelayOptions>, RelayOptionsValidator>();
        services.AddOptions<NoCtfBotOptions>()
            .Bind(configuration.GetSection(NoCtfBotOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<RelayOptions>()
            .Bind(configuration.GetSection(RelayOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<NoCtfClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<NoCtfBotOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("NoCTF.Bot/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        services.AddSingleton<BotStateStore>();
        services.AddSingleton<BotRuntimeState>();
        services.AddSingleton<BroadcastMessageFormatter>();
        services.AddSingleton<SlidingWindowLimiter>();
        services.AddSingleton<ChatProviderCatalog>();
        services.AddSingleton<OutboundMessageQueue>();
        services.AddSingleton<BotCommandProcessor>();

        services.AddSingleton<CompetitionRefreshService>();
        services.AddSingleton<ICompetitionRefreshScheduler>(provider =>
            provider.GetRequiredService<CompetitionRefreshService>());
        services.AddSingleton<CompetitionSignalRService>();
        services.AddSingleton<ICompetitionSubscriptionMonitor>(provider =>
            provider.GetRequiredService<CompetitionSignalRService>());

        services.AddHostedService<BotStartupValidationService>();
        services.AddHostedService(provider => provider.GetRequiredService<CompetitionRefreshService>());
        services.AddHostedService(provider => provider.GetRequiredService<CompetitionSignalRService>());
        services.AddHostedService<PeriodicSnapshotService>();
        services.AddHostedService<ChatEventService>();
        services.AddHostedService<OutboundMessageDispatcher>();
        return services;
    }
}
