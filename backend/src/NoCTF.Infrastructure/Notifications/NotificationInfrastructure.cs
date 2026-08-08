using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Notifications;
using NoCTF.Infrastructure.Competitions.Permissions;

namespace NoCTF.Infrastructure.Notifications;

internal static class NotificationInfrastructure
{
    internal static IServiceCollection AddNoCtfNotifications(
        this IServiceCollection services,
        bool development)
    {
        services.AddScoped<ICompetitionHubAccess, CompetitionHubAccess>();
        services.AddScoped<INotificationReader, NotificationReader>();
        services.AddScoped<ListNotifications>();
        services.AddScoped<ReadNotificationFeed>();
        services.AddScoped<CompetitionNotificationAudienceResolver>();
        services.AddScoped<CompetitionNotificationDelivery>();
        if (!development)
        {
            services.AddSingleton<ILeaderboardRefreshPublisher, RedisLeaderboardRefreshPublisher>();
            services.AddSingleton<ISubmissionResultNotification, RedisSubmissionResultNotification>();
        }
        return services;
    }
}
