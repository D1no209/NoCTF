using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Events;
using NoCTF.Application.Notifications;
using NoCTF.Application.QqBot;
using NoCTF.Application.Scoring;

namespace NoCTF.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNoCtfApplicationCore(this IServiceCollection services)
    {
        services.AddScoped<ICompetitionModeRegistry, CompetitionModeRegistry>();
        services.AddScoped<ICompetitionFileActionRegistry, CompetitionFileActionRegistry>();
        services.AddScoped<IChallengeSubmissionHandlerRegistry, ChallengeSubmissionHandlerRegistry>();
        services.AddScoped<IChallengeFeatureRegistry, ChallengeFeatureRegistry>();
        services.AddScoped<IChallengeAdminFeatureRegistry, ChallengeAdminFeatureRegistry>();
        services.AddScoped<ICompetitionJobRegistry, CompetitionJobRegistry>();
        services.AddScoped<ICompetitionJobHandler, CtfScoreRebuildJobHandler>();
        services.AddScoped<ICompetitionNotificationSink, UserNotificationOutbox>();
        services.AddScoped<ICompetitionNotificationOutbox, CompositeCompetitionNotificationOutbox>();
        services.AddScoped<IUserNotificationService, UserNotificationService>();
        services.AddScoped<IQqBotAdministrationService, UnavailableQqBotService>();
        services.AddScoped<IQqBotAgentService, UnavailableQqBotService>();
        services.AddSingleton<ICompetitionExecutionLease, CompetitionExecutionLease>();
        services.AddScoped<ICompetitionScoringProfileResolver, CompetitionScoringProfileResolver>();
        services.AddScoped<IScoreSignalEmitter, ScoreSignalEmitter>();
        services.AddScoped<IScoreEventWriter, ScoreEventWriter>();
        services.AddScoped<ICtfScoreRebuilder, CtfScoreRebuilder>();
        services.AddScoped<IScoringStrategy, DecaySolveScoringStrategy>();
        services.AddScoped<IScoringStrategy, BloodBonusScoringStrategy>();
        services.AddScoped<IScoringStrategy, RoundAccumulationScoringStrategy>();
        services.AddScoped<IScoringStrategy, OneShotVerificationScoringStrategy>();
        services.AddScoped<IScoringStrategy, ControlIntervalScoringStrategy>();
        return services;
    }
}
