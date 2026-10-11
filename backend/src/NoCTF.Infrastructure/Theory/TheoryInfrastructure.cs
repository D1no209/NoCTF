using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Theory.Questions;
using NoCTF.Infrastructure.Theory.Questions;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;
using NoCTF.Infrastructure.Theory.Activities;
using NoCTF.Infrastructure.Theory.Papers;

namespace NoCTF.Infrastructure.Theory;

internal static class TheoryInfrastructure
{
    internal static IServiceCollection AddNoCtfTheory(this IServiceCollection services, bool development)
    {
        services.AddScoped<ITheoryQuestionBank, TheoryQuestionBank>();
        services.AddSingleton<NoCTF.Infrastructure.Theory.Realtime.TheoryChangeAudienceResolver>();
        if (development) services.AddSingleton<NoCTF.Application.Theory.Realtime.ITheoryChangePublisher, NoCTF.Infrastructure.Theory.Realtime.NoOpTheoryChangePublisher>();
        else services.AddSingleton<NoCTF.Application.Theory.Realtime.ITheoryChangePublisher, NoCTF.Infrastructure.Theory.Realtime.NatsTheoryChangePublisher>();
        services.AddScoped<ManageTheoryQuestions>();
        services.AddScoped<ITheoryImageStore, TheoryImageStore>();
        services.AddScoped<ManageTheoryImages>();
        services.AddScoped<ITheoryActivityStore, TheoryActivityStore>();
        services.AddScoped<ITheoryPaperStore, TheoryPaperStore>();
        services.AddScoped<ITheoryReviewStore, TheoryReviewStore>();
        services.AddScoped<NoCTF.Infrastructure.Messaging.IClusterScheduleContributor, TheoryScheduleSource>();
        return services;
    }
}
