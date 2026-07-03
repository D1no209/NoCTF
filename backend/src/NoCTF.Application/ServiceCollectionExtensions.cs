using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Scoring;

namespace NoCTF.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNoCtfApplicationCore(this IServiceCollection services)
    {
        services.AddScoped<ICompetitionModeRegistry, CompetitionModeRegistry>();
        services.AddScoped<ICompetitionJobRegistry, CompetitionJobRegistry>();
        services.AddScoped<ICompetitionScoringProfileResolver, CompetitionScoringProfileResolver>();
        services.AddScoped<IScoreSignalEmitter, ScoreSignalEmitter>();
        services.AddScoped<IScoreEventWriter, ScoreEventWriter>();
        services.AddScoped<IScoringStrategy, DecaySolveScoringStrategy>();
        services.AddScoped<IScoringStrategy, BloodBonusScoringStrategy>();
        services.AddScoped<IScoringStrategy, RoundAccumulationScoringStrategy>();
        services.AddScoped<IScoringStrategy, OneShotVerificationScoringStrategy>();
        services.AddScoped<IScoringStrategy, ControlIntervalScoringStrategy>();
        return services;
    }
}
