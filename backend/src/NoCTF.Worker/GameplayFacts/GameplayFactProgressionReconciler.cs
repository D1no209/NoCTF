using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Competitions.Progression;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker;

public sealed class GameplayFactProgressionReconciler(IDbContextFactory<NoCtfDbContext> contexts, TimeProvider clock)
{
    public async Task ReconcileAsync(CompetitionEventCommitted message, CancellationToken ct)
    {
        if (message.Kind != CompetitionEventKind.GameplayFactAdjudicated) return;
        for (var attempt = 0; ; attempt++)
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            try
            {
                var target = await db.CompetitionEvents.AsNoTracking()
                    .Where(item => item.Id == message.EventId && item.CompetitionId == message.CompetitionId
                        && item.Kind == CompetitionEventKind.GameplayFactAdjudicated
                        && (item.GameplayFactKind == GameplayFactKind.FlagAttempt || item.GameplayFactKind == GameplayFactKind.FixAttempt)
                        && db.Competitions.Any(competition => competition.Id == item.CompetitionId && competition.Mode == GameMode.Ctf)
                        && db.CompetitionProgressions.Any(graph => graph.CompetitionId == item.CompetitionId && graph.Enabled))
                    .Select(item => item.TeamId).SingleOrDefaultAsync(ct);
                if (target is not Guid teamId) return;
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                await new ProgressionReconciler(db).ReconcilePersistedTeamAsync(message.CompetitionId, teamId, clock.GetUtcNow(), ct);
                await transaction.CommitAsync(ct);
                return;
            }
            catch (Exception exception) when (attempt < 2 && TransactionFailureClassifier.IsRetryable(exception))
            {
                // A fresh context rereads current facts after a competing correction.
            }
        }
    }
}
