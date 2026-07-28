using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Permissions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Competitions.Administration;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Competitions.Configuration;
using NoCTF.Infrastructure.Competitions.Koh;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Infrastructure.Competitions.Permissions;

namespace NoCTF.Infrastructure.Competitions;

internal static class CompetitionInfrastructure
{
    internal static IServiceCollection AddNoCtfCompetitions(this IServiceCollection services)
    {
        services.AddScoped<ICompetitionLifecycleStore, CompetitionLifecycleStore>();
        services.AddScoped<IAwdRoundCoordinator, PostgresAwdRoundCoordinator>();
        services.AddScoped<IAwdRuntimeProvisioner, PostgresAwdRuntimeProvisioner>();
        services.AddScoped<IKohRuntimeProvisioner, PostgresKohRuntimeProvisioner>();
        services.AddScoped<IKohChallengeAccessReader, PostgresKohChallengeAccessReader>();
        services.AddSingleton<AwdRoundConfigurationCatalog>();
        services.AddSingleton<AwdCheckerConfigurationCatalog>();
        services.AddSingleton<KohProducerConfigurationCatalog>();
        services.AddHttpClient(HttpKohControlClient.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
        services.AddTransient<IKohControlClient, HttpKohControlClient>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICompetitionStartGateStore, CompetitionStartGateStore>();
        services.AddScoped<CompetitionStartGate>();
        services.AddScoped<ICompetitionManagementStore, CompetitionManagementStore>();
        services.AddScoped<CreateCompetition>();
        services.AddScoped<GetCompetition>();
        services.AddScoped<ListCompetitions>();
        services.AddScoped<UpdateCompetition>();
        services.AddScoped<DeleteCompetition>();
        services.AddScoped<IAdminCompetitionStore, AdminCompetitionStore>();
        services.AddScoped<ListAdminCompetitions>();
        services.AddScoped<GetAdminCompetition>();
        services.AddScoped<RestoreCompetition>();
        services.AddScoped<HardDeleteCompetition>();
        services.AddScoped<TransferCompetitionOwner>();
        services.AddScoped<ICompetitionConfigurationStore, CompetitionConfigurationStore>();
        services.AddSingleton<ICompetitionConfigurationValidator, GameModeCompetitionConfigurationValidator>();
        services.AddScoped<GetCompetitionConfiguration>();
        services.AddScoped<UpdateCompetitionConfiguration>();
        services.AddScoped<ICompetitionPermissionStore, CompetitionPermissionStore>();
        services.AddScoped<UpdateCompetitionPermissions>();
        services.AddScoped<NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycleUseCase>();
        services.AddScoped<TransitionCompetitionLifecycle>();
        return services;
    }
}
