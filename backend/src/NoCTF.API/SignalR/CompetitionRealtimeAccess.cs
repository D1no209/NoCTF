using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Core;
using NoCTF.Infrastructure;
using System.Security.Claims;

namespace NoCTF.API.SignalR;

internal static class CompetitionRealtimeAccess
{
    public static async Task<bool> CanJoinCompetitionGroupAsync(
        HubCallerContext context,
        ApplicationDbContext db,
        ICompetitionPermissionService permissions,
        Guid competitionId)
    {
        var userIdClaim = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            return false;

        if (await permissions.CanManageCompetitionAsync(userId, competitionId, context.ConnectionAborted))
            return true;

        var now = DateTime.UtcNow;
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == competitionId)
            .Select(c => new { c.Status, c.StartTime })
            .FirstOrDefaultAsync(context.ConnectionAborted);

        if (competition is null ||
            competition.Status is not (CompetitionStatus.Published or CompetitionStatus.Running or CompetitionStatus.Finished))
        {
            return false;
        }

        if (competition.Status != CompetitionStatus.Finished && now < competition.StartTime)
            return false;

        return await db.TeamMembers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(tm => tm.CompetitionId == competitionId && tm.UserId == userId)
            .Join(
                db.Teams.IgnoreQueryFilters().AsNoTracking().Where(t =>
                    t.CompetitionId == competitionId &&
                    t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !t.IsBanned),
                tm => tm.TeamId,
                t => t.Id,
                (_, _) => true)
            .AnyAsync(context.ConnectionAborted);
    }
}
