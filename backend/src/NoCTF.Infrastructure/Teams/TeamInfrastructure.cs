using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;
using NoCTF.Application.Teams.Appeals;
using NoCTF.Infrastructure.Teams.Appeals;
using NoCTF.Infrastructure.Teams.Membership;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Infrastructure.Teams.Registration;
using NoCTF.Application.Teams.WriteUps;
using NoCTF.Infrastructure.Teams.WriteUps;

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
        services.AddScoped<ListCompetitionTeams>();
        services.AddScoped<ReviewTeamRegistration>();
        services.AddScoped<SubmitTeamRegistration>();
        services.AddScoped<GetTeam>();
        services.AddScoped<GetMyTeam>();
        services.AddScoped<ITeamWriteUpStore, TeamWriteUpStore>();
        services.AddScoped<ManageTeamWriteUps>();
        services.AddScoped<UpdateTeam>();
        services.AddScoped<DeleteTeam>();
        services.AddScoped<ITeamMembershipStore, TeamMembershipStore>();
        services.AddScoped<IAdminTeamInvitationReader, AdminTeamInvitationReader>();
        services.AddScoped<JoinTeamByInvitation>();
        services.AddScoped<GetTeamInvitation>();
        services.AddScoped<GetAdminTeamInvitation>();
        services.AddScoped<RotateTeamInvitation>();
        services.AddScoped<RemoveTeamMember>();
        services.AddScoped<LeaveTeam>();
        services.AddScoped<TransferTeamCaptain>();
        return services;
    }
}
