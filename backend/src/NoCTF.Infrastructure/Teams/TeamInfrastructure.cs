using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;
using NoCTF.Infrastructure.Teams.Membership;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Infrastructure.Teams.Registration;

namespace NoCTF.Infrastructure.Teams;

internal static class TeamInfrastructure
{
    internal static IServiceCollection AddNoCtfTeams(this IServiceCollection services)
    {
        services.AddScoped<ITeamModerationStore, TeamModerationStore>();
        services.AddScoped<ICompetitionModerationAuthorizer, CompetitionModerationAuthorizer>();
        services.AddScoped<ITeamRegistrationStore, TeamRegistrationStore>();
        services.AddScoped<CreateTeam>();
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
        return services;
    }
}
