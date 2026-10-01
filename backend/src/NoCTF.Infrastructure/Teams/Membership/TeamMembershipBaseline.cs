using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Teams.Membership;

/// <summary>Initializes active membership identities before request listeners start.</summary>
public static class TeamMembershipBaseline
{
    public static async Task InitializeAsync(NoCtfDbContext db, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await InitializeOnceAsync(db, ct);
                return;
            }
            catch (Exception exception) when (attempt < 2
                && (exception is DbUpdateException || RelationalRetry.IsTransientConcurrency(exception)))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(5, 31), ct);
            }
        }
    }

    private static async Task InitializeOnceAsync(NoCtfDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await db.Teams.IgnoreQueryFilters().AnyAsync(team => team.Members.Any(member =>
                member.CompetitionId != team.CompetitionId
                || team.DeletedAt == null && member.ActiveMembership == null
                || team.DeletedAt != null && member.ActiveMembership != null), ct))
            return;

        await db.Teams.IgnoreQueryFilters().AsSplitQuery().LoadAsync(ct);
        // Save synchronizes the same membership graph used by normal mutations.
        // Existing competing active memberships fail the unique key, rather than choosing an owner.
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }
}
