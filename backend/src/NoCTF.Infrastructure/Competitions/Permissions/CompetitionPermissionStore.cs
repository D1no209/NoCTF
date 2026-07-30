using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Permissions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Administration;

namespace NoCTF.Infrastructure.Competitions.Permissions;

public sealed class CompetitionPermissionStore(NoCtfDbContext db) : ICompetitionPermissionStore
{
    public async Task<CompetitionPermissionUpdateResult> UpdateAsync(
        UpdateCompetitionPermissionsCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        var competition = await db.Competitions.SingleOrDefaultAsync(
            item => item.Id == command.CompetitionId && item.DeletedAt == null,
            ct);
        if (competition is null)
            return new(CompetitionPermissionUpdateState.NotFound);

        var administrator = await db.Users.AsNoTracking().AnyAsync(
            user => user.Id == command.ActorId && user.Role == UserRole.Administrator,
            ct);
        if (competition.OwnerId != command.ActorId && !administrator)
            return new(CompetitionPermissionUpdateState.Forbidden);

        var userIds = command.ManagerIds
            .Concat(command.JudgeIds)
            .Concat(command.ObserverIds)
            .ToArray();
        if (userIds.Length != userIds.Distinct().Count())
            return new(CompetitionPermissionUpdateState.RolesOverlap);
        if (userIds.Contains(competition.OwnerId))
        {
            return new(
                CompetitionPermissionUpdateState.OwnerIncluded,
                [competition.OwnerId]);
        }

        var eligibility = await ResourceManagerRoleGuard.AcquireAndCheckAsync(
            db,
            command.ManagerIds.Append(competition.OwnerId),
            ct);
        if (eligibility.MissingUserIds.Length > 0)
        {
            return new(
                CompetitionPermissionUpdateState.UserNotFound,
                eligibility.MissingUserIds);
        }
        if (eligibility.RoleIneligibleUserIds.Length > 0)
        {
            return new(
                CompetitionPermissionUpdateState.RoleNotEligible,
                eligibility.RoleIneligibleUserIds);
        }

        var normalizedUserIds = userIds.Distinct().Order().ToArray();
        var existingUserIds = await db.Users.AsNoTracking()
            .Where(user => normalizedUserIds.Contains(user.Id))
            .Select(user => user.Id)
            .ToArrayAsync(ct);
        var missingUserIds = normalizedUserIds.Except(existingUserIds).ToArray();
        if (missingUserIds.Length > 0)
        {
            return new(
                CompetitionPermissionUpdateState.UserNotFound,
                missingUserIds);
        }

        competition.ManagerIds = command.ManagerIds.Distinct().Order().ToArray();
        competition.JudgeIds = command.JudgeIds.Distinct().Order().ToArray();
        competition.ObserverIds = command.ObserverIds.Distinct().Order().ToArray();
        competition.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(CompetitionPermissionUpdateState.Updated);
    }
}
