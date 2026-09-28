using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NATS.Client.Core;
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
        services.AddScoped<NotificationChangeAudienceResolver>();
        services.AddScoped<CompetitionNotificationDelivery>();
        services.AddScoped<ICompetitionAnnouncementPublisher>(provider =>
            provider.GetRequiredService<CompetitionNotificationDelivery>());
        services.AddScoped<PublishCompetitionAnnouncement>();
        if (!development && services.Any(descriptor => descriptor.ServiceType == typeof(INatsConnection)))
            services.AddSingleton<INotificationChangePublisher, NatsNotificationChangePublisher>();
        else
            services.AddSingleton<INotificationChangePublisher, NoOpNotificationChangePublisher>();
        if (!development)
        {
            services.AddSingleton<ILeaderboardRefreshPublisher, NatsLeaderboardRefreshPublisher>();
            services.AddSingleton<IGameplayFactStateChangedNotification, NatsGameplayFactStateChangedNotification>();
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
