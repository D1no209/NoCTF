using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Theory.Questions;
using NoCTF.Infrastructure.Theory.Questions;

namespace NoCTF.Infrastructure.Theory;

internal static class TheoryInfrastructure
{
    internal static IServiceCollection AddNoCtfTheory(this IServiceCollection services)
    {
        services.AddScoped<ITheoryQuestionBank, TheoryQuestionBank>();
        services.AddScoped<ManageTheoryQuestions>();
        return services;
    }
}
