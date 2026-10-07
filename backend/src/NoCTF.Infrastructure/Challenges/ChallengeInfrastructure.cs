using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Attachments;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Challenges.Configuration;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Challenges.Hints;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Application.Challenges.Questions;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Infrastructure.Challenges.Questions;
using NoCTF.Infrastructure.Challenges.Testing;
using NoCTF.Infrastructure.Caching;

namespace NoCTF.Infrastructure.Challenges;

internal static class ChallengeInfrastructure
{
    internal static IServiceCollection AddNoCtfChallenges(this IServiceCollection services)
    {
        services.AddScoped<TeamChallengeCriticalSection>();
        services.AddSingleton<IChallengeConfigurationCatalog, GameModeChallengeConfigurationCatalog>();
        services.AddSingleton<ChallengeRuntimeTemplateCatalog>();
        services.AddSingleton<IChallengeRuntimeTemplateCatalog,
            FusionChallengeRuntimeTemplateCatalog>();
        services.AddScoped<IPerTeamRuntimeFlagStore, PerTeamRuntimeFlagStore>();
        services.AddScoped<ICompetitionChallengeReadAccess, CompetitionChallengeReadAccess>();
        services.AddScoped<IChallengeManagementStore, ChallengeManagementStore>();
        services.AddScoped<IChallengeBankStore, ChallengeBankStore>();
        services.AddScoped<CreateChallengeTemplate>();
        services.AddScoped<ListChallengeTemplates>();
        services.AddScoped<GetChallengeTemplate>();
        services.AddScoped<UpdateChallengeTemplate>();
        services.AddScoped<DeleteChallengeTemplate>();
        services.AddScoped<RestoreChallengeTemplate>();
        services.AddScoped<UpdateChallengeTemplatePermissions>();
        services.AddScoped<TransferChallengeTemplateOwner>();
        services.AddScoped<IChallengeTestRuntimeStore, ChallengeTestRuntimeStore>();
        services.AddScoped<GetChallengeTestRuntime>();
        services.AddScoped<MutateChallengeTestRuntime>();
        services.AddScoped<IChallengeAttachmentStore, ChallengeAttachmentStore>();
        services.AddScoped<ManageChallengeAttachments>();
        services.AddScoped<GetChallengeAttachments>();
        services.AddScoped<PrepareChallengeAttachmentDownload>();
        services.AddScoped<IChallengeFlagStore, ChallengeFlagManagementStore>();
        services.AddScoped<ManageChallengeFlags>();
        services.AddScoped<IMissingFlagGenerator, MissingFlagGenerator>();
        services.AddScoped<GenerateMissingFlags>();
        services.AddScoped<IChallengeHintStore, ChallengeHintStore>();
        services.AddScoped<ManageChallengeHints>();
        services.AddScoped<UnlockChallengeHint>();
        services.AddScoped<IParticipantChallengeHintStore, ParticipantChallengeHintStore>();
        services.AddScoped<ReadParticipantChallengeHints>();
        services.AddScoped<CreateChallenge>();
        services.AddScoped<GetChallenge>();
        services.AddScoped<ListChallenges>();
        services.AddScoped<UpdateChallenge>();
        services.AddScoped<DeleteChallenge>();
        services.AddScoped<IChallengeConfigurationStore, ChallengeConfigurationStore>();
        services.AddScoped<GetChallengeConfiguration>();
        services.AddScoped<UpdateChallengeConfiguration>();
        services.AddScoped<CompetitionQuestionStore>();
        services.AddScoped<CompetitionQuestionAccessResolver>();
        services.AddScoped<ICompetitionQuestionStore>(provider =>
            provider.GetRequiredService<CompetitionQuestionStore>());
        services.AddScoped<ICompetitionQuestionReader, CompetitionQuestionQueryReader>();
        services.AddScoped<ICompetitionQuestionWriter, CompetitionQuestionTransactionWriter>();
        services.AddScoped<CreateCompetitionQuestion>();
        services.AddScoped<CreateTeamWriteUpConsultation>();
        services.AddScoped<ListCompetitionQuestions>();
        services.AddScoped<GetCompetitionQuestion>();
        services.AddScoped<AddCompetitionQuestionMessage>();
        services.AddScoped<ChangeCompetitionQuestionStatus>();
        return services;
    }
}
