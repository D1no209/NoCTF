using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Infrastructure.LiveSolo.Matches;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.LiveSolo.Resources;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.LiveSolo.Rounds;
using NoCTF.Application.LiveSolo.Templates;
using NoCTF.Infrastructure.LiveSolo.Templates;
using NoCTF.Application.Challenges.Configuration;

namespace NoCTF.Infrastructure.LiveSolo;

public static class LiveSoloInfrastructure
{
    public static IServiceCollection AddNoCtfLiveSolo(this IServiceCollection services)
    {
        services.AddScoped<ILiveSoloMatchStore, LiveSoloMatchStore>();
        services.AddScoped<ManageLiveSoloMatches>();
        services.AddScoped<ILiveSoloTemplateCopyStore, LiveSoloTemplateCopyStore>();
        services.AddScoped<CopyLiveSoloTemplate>();
        services.AddScoped<IChallengeMaterialMutationGate, LiveSoloMaterialMutationGate>();
        services.AddScoped<IExecutionScopeAccess, LiveSoloExecutionAccess>();
        services.AddScoped<IScopedRuntimeControl, ScopedRuntimeControl>();
        services.AddScoped<ILiveSoloRuntimePreparation, LiveSoloRuntimePreparation>();
        services.AddScoped<ILiveSoloAttachmentStore, LiveSoloAttachmentStore>();
        services.AddScoped<AccessLiveSoloAttachments>();
        services.AddScoped<IClusterScheduleContributor, LiveSoloScheduleSource>();
        services.TryAddSingleton<ILiveSoloMediaGateway, UnconfiguredLiveSoloMediaGateway>();
        return services;
    }
}
