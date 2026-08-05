using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.GameModes.Awdp.Scoring;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Scoring.Leaderboard;

namespace NoCTF.Infrastructure.Scoring;

internal static class ScoringInfrastructure
{
    internal static IServiceCollection AddNoCtfScoring(this IServiceCollection services)
    {
        services.AddSingleton<ILeaderboardProjectorCatalog, LeaderboardProjectorCatalog>();
        services.AddSingleton<ILeaderboardProjectionEngine, LeaderboardProjectionEngine>();
        services.AddSingleton<ILeaderboardSubscriptionRegistry, RedisLeaderboardSubscriptionRegistry>();
        services.AddScoped<RedisLeaderboardCache>();
        services.AddScoped<ILeaderboardCache>(provider =>
            provider.GetRequiredService<RedisLeaderboardCache>());
        services.AddScoped<ILeaderboardSnapshotFactory>(provider =>
            provider.GetRequiredService<RedisLeaderboardCache>());
        return services;
    }
}
