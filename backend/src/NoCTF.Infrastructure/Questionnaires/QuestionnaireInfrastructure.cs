using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Questionnaires.Images;
using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;
using NoCTF.Infrastructure.Questionnaires.Activities;
using NoCTF.Infrastructure.Questionnaires.Sessions;

namespace NoCTF.Infrastructure.Questionnaires;

internal static class QuestionnaireInfrastructure
{
    internal static IServiceCollection AddNoCtfQuestionnaires(this IServiceCollection services, bool development)
    {
        services.AddSingleton<Realtime.QuestionnaireChangeAudienceResolver>();
        if (development) services.AddSingleton<NoCTF.Application.Questionnaires.Realtime.IQuestionnaireChangePublisher, Realtime.NoOpQuestionnaireChangePublisher>();
        else services.AddSingleton<NoCTF.Application.Questionnaires.Realtime.IQuestionnaireChangePublisher, Realtime.NatsQuestionnaireChangePublisher>();
        services.AddScoped<IQuestionnaireImageStore, QuestionnaireImageStore>();
        services.AddScoped<ManageQuestionnaireImages>();
        services.AddScoped<IQuestionnaireActivityStore, QuestionnaireActivityStore>();
        services.AddScoped<IQuestionnaireSessionStore, QuestionnaireSessionStore>();
        services.AddScoped<IQuestionnaireReviewStore, QuestionnaireReviewStore>();
        services.AddScoped<NoCTF.Infrastructure.Messaging.IClusterScheduleContributor, QuestionnaireScheduleSource>();
        return services;
    }
}
