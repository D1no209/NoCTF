using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.GameModes.Awdp.Scoring;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.Application.Scoring.Awdp;
using NoCTF.Infrastructure.Scoring.Awdp;

namespace NoCTF.Infrastructure.Scoring;

internal static class ScoringInfrastructure
{
    internal static IServiceCollection AddNoCtfScoring(this IServiceCollection services)
    {
        services.AddSingleton<ILeaderboardProjectorCatalog, LeaderboardProjectorCatalog>();
        services.AddSingleton<ILeaderboardProjectionEngine, LeaderboardProjectionEngine>();
        services.AddScoped<ILeaderboardCache, FusionLeaderboardCache>();
        services.AddScoped<ILeaderboardSnapshotFactory, FusionLeaderboardCache>();
        services.AddScoped<IAwdpScoringImpactPreviewStore, AwdpScoringImpactPreviewStore>();
        services.AddScoped<PreviewAwdpScoringImpact>();
        return services;
    }
}
