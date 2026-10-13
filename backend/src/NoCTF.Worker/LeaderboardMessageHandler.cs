using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker;

public sealed class LeaderboardMessageHandler(ILeaderboardCache leaderboard, NoCtfDbContext db, TimeProvider clock)
{
    public async Task Handle(
        ProjectLeaderboard message,
        CancellationToken cancellationToken)
    {
        if (message.Mode == LeaderboardRefreshMode.Force)
            await leaderboard.RefreshAsync(message.CompetitionId, cancellationToken);
        else if (message.Mode != LeaderboardRefreshMode.IfMissing)
            throw new ArgumentOutOfRangeException(nameof(message));
        var projection = await leaderboard.GetScoreboardAsync(message.CompetitionId, cancellationToken);
        if (projection is null)
        {
            if (message.Mode == LeaderboardRefreshMode.IfMissing
                && await db.Competitions.AnyAsync(item => item.Id == message.CompetitionId
                    && item.Mode != NoCTF.Domain.Competitions.GameMode.LiveSolo, cancellationToken))
                throw new TimeoutException("The invalidated leaderboard is not published yet.");
            return;
        }
        foreach (var batch in projection.TheoryReceipts.Chunk(128))
        {
            var fact = System.Linq.Expressions.Expression.Parameter(typeof(GameplayFact), "fact");
            System.Linq.Expressions.Expression matches = System.Linq.Expressions.Expression.Constant(false);
            foreach (var receipt in batch)
                matches = System.Linq.Expressions.Expression.OrElse(matches, System.Linq.Expressions.Expression.AndAlso(
                    System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Property(fact, nameof(GameplayFact.Id)), System.Linq.Expressions.Expression.Constant(receipt.FactId)),
                    System.Linq.Expressions.Expression.Equal(System.Linq.Expressions.Expression.Property(fact, nameof(GameplayFact.ConcurrencyStamp)), System.Linq.Expressions.Expression.Constant(receipt.ConcurrencyStamp))));
            var exact = System.Linq.Expressions.Expression.Lambda<Func<GameplayFact, bool>>(matches, fact);
            var completionStamp = Guid.NewGuid();
            await db.GameplayFacts.Where(exact).Where(fact => fact.CompetitionId == message.CompetitionId
                && fact.Kind == GameplayFactKind.TheoryPaperSubmission && fact.State == GameplayFactState.Queued)
                .ExecuteUpdateAsync(setters => setters.SetProperty(fact => fact.State, GameplayFactState.Completed)
                    .SetProperty(fact => fact.ConcurrencyStamp, completionStamp), cancellationToken);
        }
        var included = projection.Snapshot.Teams.SelectMany(team => team.ChallengeBenefits
            .Where(x => x.WriteUpUnlockedAt is not null).Select(x => (team.TeamId, x.CompetitionChallengeId, x.WriteUpUnlockedAt))).ToHashSet();
        var queued = await db.WriteUpUnlockReceipts.AsNoTracking().Where(x => x.CompetitionId == message.CompetitionId
            && db.GameplayFacts.Any(f => f.Id == x.GameplayFactId && f.State == GameplayFactState.Queued))
            .OrderBy(x => x.UnlockedAt).ThenBy(x => x.GameplayFactId).Take(256)
            .Select(x => new { x.GameplayFactId, x.TeamId, x.CompetitionChallengeId, x.UnlockedAt }).ToArrayAsync(cancellationToken);
        var ids = queued.Where(x => included.Contains((x.TeamId, x.CompetitionChallengeId, (DateTimeOffset?)x.UnlockedAt)))
            .Select(x => x.GameplayFactId).ToArray();
        if (ids.Length == 0) return;
        var now = clock.GetUtcNow(); var stamp = Guid.NewGuid();
        await db.GameplayFacts.Where(x => ids.Contains(x.Id) && x.State == GameplayFactState.Queued)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.State, GameplayFactState.Completed)
                .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.ConcurrencyStamp, stamp), cancellationToken);
    }
}
