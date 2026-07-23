using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Permissions;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfCompetitionPermissionStore(NoCtfDbContext db) : ICompetitionPermissionStore
{
    public async Task<CompetitionPermissionUpdateState> UpdateAsync(
        UpdateCompetitionPermissionsCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        var competition = await db.Competitions.SingleOrDefaultAsync(
            item => item.Id == command.CompetitionId && item.DeletedAt == null,
            ct);
        if (competition is null)
            return CompetitionPermissionUpdateState.NotFound;

        var administrator = await db.Users.AsNoTracking().AnyAsync(
            user => user.Id == command.ActorId && user.Role == UserRole.Administrator,
            ct);
        if (competition.OwnerId != command.ActorId && !administrator)
            return CompetitionPermissionUpdateState.Forbidden;

        var userIds = command.ManagerIds
            .Concat(command.JudgeIds)
            .Concat(command.ObserverIds)
            .Distinct()
            .ToArray();
        if (userIds.Contains(competition.OwnerId))
            return CompetitionPermissionUpdateState.InvalidUser;
        var existing = await db.Users.AsNoTracking()
            .CountAsync(user => userIds.Contains(user.Id), ct);
        if (existing != userIds.Length)
            return CompetitionPermissionUpdateState.InvalidUser;

        competition.ManagerIds = command.ManagerIds.Distinct().ToArray();
        competition.JudgeIds = command.JudgeIds.Distinct().ToArray();
        competition.ObserverIds = command.ObserverIds.Distinct().ToArray();
        competition.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return CompetitionPermissionUpdateState.Updated;
    }
}
