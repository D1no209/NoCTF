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

    public async Task<string?> AddOrUpdateAsync(AddCompetitionCollaboratorCommand command, CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.CompetitionId && !x.Deletion.IsDeleted, ct);
        if (competition is null) return "competition_not_found";
        if (competition.OwnerId == command.UserId) return "owner_is_not_collaborator";
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == command.UserId, ct)) return "user_not_found";
        var existing = await db.CompetitionCollaborators.SingleOrDefaultAsync(x => x.CompetitionId == command.CompetitionId && x.UserId == command.UserId, ct);
        if (existing is null)
            db.CompetitionCollaborators.Add(new CompetitionCollaborator { Id = Guid.CreateVersion7(command.AddedAt), CompetitionId = command.CompetitionId, UserId = command.UserId, Role = command.Role, AddedAt = command.AddedAt });
        else
            existing.Role = command.Role;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<bool> RemoveAsync(Guid competitionId, Guid userId, CancellationToken ct)
    {
        var entity = await db.CompetitionCollaborators.SingleOrDefaultAsync(x => x.CompetitionId == competitionId && x.UserId == userId, ct);
        if (entity is null) return false;
        db.CompetitionCollaborators.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
