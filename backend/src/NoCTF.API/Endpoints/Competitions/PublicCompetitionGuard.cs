using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Competitions;

internal static class PublicCompetitionGuard
{
    public static bool IsPublic(CompetitionStatus status)
        => status is CompetitionStatus.Published
            or CompetitionStatus.Running
            or CompetitionStatus.Paused
            or CompetitionStatus.Finished;

    public static string? ResolvePlayBlockReason(Competition competition, DateTime now)
    {
        if (now < competition.StartTime || competition.Status == CompetitionStatus.Draft)
            return "competition_not_started";
        if (now > competition.EndTime || competition.Status == CompetitionStatus.Finished)
            return "competition_ended";
        if (competition.Status == CompetitionStatus.Paused)
            return "competition_paused";
        return null;
    }

    public static async Task<bool> ExistsAsync(
        ApplicationDbContext db,
        Guid competitionId,
        CancellationToken ct,
        GameModeType? gameModeType = null)
    {
        var query = db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == competitionId);

        if (gameModeType.HasValue)
            query = query.Where(c => c.GameModeType == gameModeType.Value);

        return await query.AnyAsync(c =>
            c.Status == CompetitionStatus.Published ||
            c.Status == CompetitionStatus.Running ||
            c.Status == CompetitionStatus.Paused ||
            c.Status == CompetitionStatus.Finished, ct);
    }
}
