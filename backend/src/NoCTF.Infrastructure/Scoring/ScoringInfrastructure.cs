using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.GameModes.Awdp.Scoring;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Scoring.Leaderboard;

namespace NoCTF.Infrastructure.Scoring;

internal static class ScoringInfrastructure
{
    internal static IServiceCollection AddNoCtfScoring(
        this IServiceCollection services,
        bool development)
    {
        services.AddSingleton<ILeaderboardProjectorCatalog, LeaderboardProjectorCatalog>();
        services.AddSingleton<ILeaderboardProjectionEngine, LeaderboardProjectionEngine>();
        services.AddSingleton<LeaderboardSnapshotDeserializer>();
        services.AddScoped<ILeaderboardCache, FusionLeaderboardCache>();
        services.AddScoped<BloodAnnouncementReconciler>();
        services.AddScoped<ILeaderboardSnapshotFactory, FusionLeaderboardCache>();
        services.AddScoped<IScoreboardDetailReader, ScoreboardDetailReader>();
        services.AddScoped<IScoreboardTrendFactReader, ScoreboardTrendFactReader>();
        services.AddScoped<BuildScoreboardTrends>();
        services.AddScoped<GetSpectatorScreen>();
        services.AddScoped<NoCTF.Application.Teams.Standings.GetMyTeamStanding>();
        if (!development)
        {
            services.AddSingleton<ILeaderboardPublicationFence, NatsLeaderboardPublicationFence>();
        }
        return services;
    }
}
