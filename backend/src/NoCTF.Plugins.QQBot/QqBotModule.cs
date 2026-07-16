using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Events;
using NoCTF.Application.QqBot;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.QQBot;

public sealed class QqBotModule : IHostAwarePluginModule
{
    public string Name => "NoCTF.Plugins.QQBot";
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
        => ConfigureServices(services, PluginHostRole.Api);

    public void ConfigureServices(IServiceCollection services, PluginHostRole hostRole)
    {
        services.AddScoped<ICompetitionNotificationSink, QqBotNotificationOutbox>();
        services.AddScoped<QqBotTemplateRenderer>();
        services.AddScoped<IQqBotAdministrationService, QqBotAdministrationService>();
        services.AddScoped<IQqBotAgentService, QqBotAgentService>();
        services.AddScoped<ICompetitionJobHandler, QqBotDispatchJobHandler>();
    }
}
