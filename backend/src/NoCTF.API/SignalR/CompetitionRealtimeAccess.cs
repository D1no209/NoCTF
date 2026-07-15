using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;
using System.Security.Claims;

namespace NoCTF.API.SignalR;

internal static class CompetitionRealtimeAccess
{
    public static async Task<bool> CanJoinCompetitionGroupAsync(
        HubCallerContext context,
        ApplicationDbContext db,
        Guid competitionId)
    {
        var userIdClaim = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            return false;

        return await BuildAccessQuery(db, competitionId, userId, DateTime.UtcNow)
            .AnyAsync(context.ConnectionAborted);
    }

    internal static IQueryable<int> BuildAccessQuery(
        ApplicationDbContext db,
        Guid competitionId,
        Guid userId,
        DateTime now)
    {
        var participantAccess = db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(competition =>
                competition.Id == competitionId &&
                (competition.Status == CompetitionStatus.Published ||
                 competition.Status == CompetitionStatus.Running ||
                 competition.Status == CompetitionStatus.Paused ||
                 competition.Status == CompetitionStatus.Finished) &&
                (competition.Status == CompetitionStatus.Finished || now >= competition.StartTime))
            .Join(
                db.Teams.IgnoreQueryFilters().AsNoTracking().Where(t =>
                    t.CompetitionId == competitionId &&
                    t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !t.IsBanned),
                competition => competition.Id,
                team => team.CompetitionId,
                (_, team) => team.Id)
            .Join(
                db.TeamMembers.IgnoreQueryFilters().AsNoTracking().Where(member =>
                    member.CompetitionId == competitionId &&
                    member.UserId == userId),
                teamId => teamId,
                member => member.TeamId,
                (_, _) => 1);

        var adminAccess = db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(competition =>
                competition.Id == competitionId &&
                db.Users.AsNoTracking().Any(user =>
                    user.Id == userId &&
                    user.Role == UserRole.Admin))
            .Select(_ => 1);
        var ownerAccess = db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(competition => competition.Id == competitionId && competition.OwnerId == userId)
            .Select(_ => 1);
        var managerAccess = db.CompetitionCollaborators
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(collaborator =>
                collaborator.CompetitionId == competitionId &&
                collaborator.UserId == userId &&
                collaborator.Role == CollaboratorRole.Manager)
            .Select(_ => 1);

        return participantAccess
            .Concat(adminAccess)
            .Concat(ownerAccess)
            .Concat(managerAccess);
    }
}
