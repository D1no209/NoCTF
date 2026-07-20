using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Collaborators;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfCompetitionCollaboratorStore(NoCtfDbContext db) : ICompetitionCollaboratorStore
{
    public async Task<bool> CanManageAsync(Guid actorId, Guid competitionId, CancellationToken ct) =>
        await db.Users.AsNoTracking().AnyAsync(x => x.Id == actorId && x.Role == UserRole.Administrator, ct)
        || await db.Competitions.AsNoTracking().AnyAsync(x => x.Id == competitionId && x.OwnerId == actorId && !x.Deletion.IsDeleted, ct);

    public async Task<IReadOnlyList<CompetitionCollaboratorView>> ListAsync(Guid competitionId, CancellationToken ct) =>
        await db.CompetitionCollaborators.AsNoTracking().Where(x => x.CompetitionId == competitionId)
            .OrderBy(x => x.AddedAt).Select(x => new CompetitionCollaboratorView(x.UserId, x.Role, x.AddedAt)).ToListAsync(ct);

    public Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken ct) =>
        db.Competitions.AsNoTracking().Where(x => x.Id == competitionId && !x.Deletion.IsDeleted)
            .Select(x => (CompetitionStatus?)x.Status).SingleOrDefaultAsync(ct);

    public async Task<CompetitionCollaboratorFailure?> AddOrUpdateAsync(AddCompetitionCollaboratorCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        if (status is null) return CompetitionCollaboratorFailure.CompetitionNotFound;
        if (status == CompetitionStatus.Finished) return CompetitionCollaboratorFailure.CompetitionFinished;
        var competition = await db.Competitions.AsNoTracking().SingleAsync(x => x.Id == command.CompetitionId, ct);
        if (competition.OwnerId == command.UserId) return CompetitionCollaboratorFailure.OwnerIsNotCollaborator;
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == command.UserId, ct)) return CompetitionCollaboratorFailure.UserNotFound;
        var existing = await db.CompetitionCollaborators.SingleOrDefaultAsync(x => x.CompetitionId == command.CompetitionId && x.UserId == command.UserId, ct);
        if (existing is null)
            db.CompetitionCollaborators.Add(new CompetitionCollaborator { Id = Guid.CreateVersion7(command.AddedAt), CompetitionId = command.CompetitionId, UserId = command.UserId, Role = command.Role, AddedAt = command.AddedAt });
        else
            existing.Role = command.Role;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return null;
    }

    public async Task<CompetitionCollaboratorFailure?> RemoveAsync(Guid competitionId, Guid userId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null) return CompetitionCollaboratorFailure.CompetitionNotFound;
        if (status == CompetitionStatus.Finished) return CompetitionCollaboratorFailure.CompetitionFinished;
        var entity = await db.CompetitionCollaborators.SingleOrDefaultAsync(x => x.CompetitionId == competitionId && x.UserId == userId, ct);
        if (entity is null) return CompetitionCollaboratorFailure.CollaboratorNotFound;
        db.CompetitionCollaborators.Remove(entity);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return null;
    }
}
