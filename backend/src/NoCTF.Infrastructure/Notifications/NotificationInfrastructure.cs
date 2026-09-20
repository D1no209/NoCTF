using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddScoped<IPublicCompetitionAnnouncementReader, PublicCompetitionAnnouncementReader>();
        services.AddScoped<ListNotifications>();
        services.AddScoped<ListPublicCompetitionAnnouncements>();
        services.AddScoped<ReadNotificationFeed>();
        services.AddScoped<CompetitionNotificationAudienceResolver>();
        services.AddScoped<CompetitionNotificationDelivery>();
        services.AddScoped<ICompetitionAnnouncementPublisher>(provider =>
            provider.GetRequiredService<CompetitionNotificationDelivery>());
        services.AddScoped<PublishCompetitionAnnouncement>();
        if (!development)
        {
            services.AddSingleton<ILeaderboardRefreshPublisher, RedisLeaderboardRefreshPublisher>();
            services.AddSingleton<IGameplayFactStateChangedNotification, RedisGameplayFactStateChangedNotification>();
        }
        else
        {
            services.TryAddSingleton<ILeaderboardRefreshPublisher, NullLeaderboardRefreshPublisher>();
            services.TryAddSingleton<IGameplayFactStateChangedNotification, NullGameplayFactStateChangedNotification>();
        }
        return services;
    }
}

internal sealed class NullLeaderboardRefreshPublisher : ILeaderboardRefreshPublisher
{
    public Task PublishAsync(
        NoCTF.Application.Scoring.Leaderboard.ScoreboardProjection projection,
        CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class NullGameplayFactStateChangedNotification : IGameplayFactStateChangedNotification
{
    public Task PublishAsync(
        GameplayFactStateChangedNotification notification,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
