using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Directions;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions.Directions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Directions;

public sealed class CompetitionDirectionStore(NoCtfDbContext db, ICompetitionEventRecorder events,
    IPostCommitMessagePublisher messages) : ICompetitionDirectionStore
{
    public async Task<IReadOnlyList<CompetitionDirectionView>?> ListAsync(Guid competitionId, CancellationToken ct)
    {
        if (!await db.Competitions.AsNoTracking().AnyAsync(item => item.Id == competitionId, ct)) return null;
        return await db.Set<CompetitionDirection>().AsNoTracking().Where(item => item.CompetitionId == competitionId)
            .OrderBy(item => item.Position).ThenBy(item => item.Id)
            .Select(item => new CompetitionDirectionView(item.Id, item.Name, item.Icon)).ToArrayAsync(ct);
    }

    public async Task<CompetitionDirectionsResult> SaveAsync(Guid competitionId,
        IReadOnlyList<CompetitionDirectionView> items, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, System.Data.IsolationLevel.Serializable, ct);
        var competition = await db.Competitions.AsSplitQuery().SingleOrDefaultAsync(item => item.Id == competitionId, ct);
        if (competition is null) return new(null, CompetitionDirectionFailure.CompetitionNotFound);
        var current = await db.Set<CompetitionDirection>().Where(item => item.CompetitionId == competitionId).ToArrayAsync(ct);
        var ids = items.Select(item => item.Id).ToHashSet();
        if (await db.Set<CompetitionDirection>().AnyAsync(item => ids.Contains(item.Id) && item.CompetitionId != competitionId, ct))
            return new(null, CompetitionDirectionFailure.InvalidCatalog);
        var removed = current.Where(item => !ids.Contains(item.Id)).Select(item => item.Id).ToArray();
        if (await db.CompetitionChallenges.IgnoreQueryFilters().AnyAsync(item => item.CompetitionId == competitionId
            && item.DirectionId != null && removed.Contains(item.DirectionId.Value), ct))
            return new(null, CompetitionDirectionFailure.DirectionInUse);
        // Vacate the unique names inside the transaction to allow renaming swaps.
        foreach (var direction in current) direction.NormalizedName = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        db.RemoveRange(current.Where(item => removed.Contains(item.Id)));
        for (var position = 0; position < items.Count; position++)
        {
            var requested = items[position];
            var entity = current.SingleOrDefault(item => item.Id == requested.Id);
            if (entity is null)
            {
                entity = new CompetitionDirection { Id = requested.Id, CompetitionId = competitionId };
                db.Add(entity);
            }
            entity.Name = requested.Name;
            entity.NormalizedName = requested.Name.ToUpperInvariant();
            entity.Icon = requested.Icon;
            entity.Position = position;
        }
        competition.UpdatedAt = now;
        await events.RecordAsync(new(competitionId, CompetitionEventKind.CompetitionUpdated,
            CompetitionEventLevel.Information, CompetitionEventVisibility.Public, now,
            CompetitionStatus: competition.Status), ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await transaction.FlushMessagesAsync(messages);
            return new(items);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new(null, CompetitionDirectionFailure.NameConflict);
        }
    }
}
