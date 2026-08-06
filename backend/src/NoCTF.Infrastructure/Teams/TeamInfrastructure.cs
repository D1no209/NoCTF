using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;
using NoCTF.Application.Teams.Appeals;
using NoCTF.Application.Teams.Profiles;
using NoCTF.Infrastructure.Teams.Appeals;
using NoCTF.Infrastructure.Teams.Membership;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Infrastructure.Teams.Registration;
using NoCTF.Infrastructure.Teams.Profiles;

namespace NoCTF.Infrastructure.Teams;

internal static class TeamInfrastructure
{
    internal static IServiceCollection AddNoCtfTeams(this IServiceCollection services)
    {
        services.AddScoped<ITeamModerationStore, TeamModerationStore>();
        services.AddScoped<ICompetitionModerationAuthorizer, CompetitionModerationAuthorizer>();
        services.AddScoped<ITeamBanAppealStore, TeamBanAppealStore>();
        services.AddScoped<GetMyTeamBanCase>();
        services.AddScoped<ListTeamBanAppeals>();
        services.AddScoped<SubmitTeamBanAppeal>();
        services.AddScoped<ResolveTeamBanAppeal>();
        services.AddScoped<CorrectTeamBan>();
        services.AddScoped<ITeamRegistrationStore, TeamRegistrationStore>();
        services.AddScoped<CreateTeam>();
        services.AddScoped<RegisterTeamProfile>();
        services.AddScoped<ListCompetitionTeams>();
        services.AddScoped<ReviewTeamRegistration>();
        services.AddScoped<ResubmitTeamRegistration>();
        services.AddScoped<GetTeam>();
        services.AddScoped<GetMyTeam>();
        services.AddScoped<UpdateTeam>();
        services.AddScoped<DeleteTeam>();
        services.AddScoped<ITeamMembershipStore, TeamMembershipStore>();
        services.AddScoped<JoinTeamByInvitation>();
        services.AddScoped<RotateTeamInvitation>();
        services.AddScoped<RemoveTeamMember>();
        services.AddScoped<LeaveTeam>();
        services.AddScoped<TransferTeamCaptain>();
        services.AddScoped<ITeamProfileStore, TeamProfileStore>();
        services.AddScoped<CreateTeamProfile>();
        services.AddScoped<ListMyTeamProfiles>();
        services.AddScoped<UpdateTeamProfile>();
        services.AddScoped<JoinTeamProfile>();
        services.AddScoped<RotateTeamProfileInvitation>();
        services.AddScoped<RemoveTeamProfileMember>();
        services.AddScoped<LeaveTeamProfile>();
        services.AddScoped<TransferTeamProfileCaptain>();
        services.AddScoped<DeleteTeamProfile>();
        return services;
    }
}
