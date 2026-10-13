using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awd.Checking;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

public sealed class AwdCheckAdjudicationStore(NoCtfDbContext db, ICompetitionEventRecorder events,
    IPostCommitMessagePublisher publisher) : IAwdCheckAdjudicationStore
{
    public async Task<AwdCheckListView> ListAsync(AwdCheckListQuery request, CancellationToken ct)
    {
        var query = db.GameplayFacts.AsNoTracking().Where(fact => fact.CompetitionId == request.CompetitionId
                && fact.Kind == GameplayFactKind.AwdServiceTransition && fact.CompetitionChallengeId != null && fact.Awd != null && fact.TeamId != null
                && (request.Settlement == null || fact.Awd.Settlement == request.Settlement)
                && (request.TeamId == null || fact.TeamId == request.TeamId)
                && (request.CompetitionChallengeId == null || fact.CompetitionChallengeId == request.CompetitionChallengeId))
            .Join(db.AwdRounds.Where(round => round.CompetitionId == request.CompetitionId
                    && (request.RoundNumber == null || round.Number == request.RoundNumber)),
                fact => fact.ReferenceId, round => (Guid?)round.Id,
                (fact, round) => new { Fact = fact, Round = round });
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(item => item.Round.Number)
            .ThenByDescending(item => item.Fact.Awd!.ScheduledAt).ThenBy(item => item.Fact.Id)
            .Skip(request.Offset).Take(request.Limit)
            .Select(item => new AwdCheckListItem(item.Fact.Id, item.Fact.CompetitionChallengeId!.Value,
                item.Fact.TeamId!.Value, item.Round.Id, item.Round.Number,
                item.Fact.Awd!.ScheduledAt ?? item.Fact.OccurredAt, item.Fact.Awd.Health,
                item.Fact.Awd.Execution, item.Fact.Awd.Settlement, item.Fact.Awd.Reason,
                item.Fact.Awd.FinalScheduledCheck, item.Fact.Awd.ReceivedAt)).ToArrayAsync(ct);
        return new(items, total);
    }

    public async Task<AwdCheckEvidenceView?> ReadAsync(Guid competitionId, Guid gameplayFactId, CancellationToken ct)
    {
        var fact = await Query(competitionId, gameplayFactId).AsNoTracking().SingleOrDefaultAsync(ct);
        return fact is null ? null : View(fact);
    }

    public async Task<AwdCheckAdjudicationResult> ApplyAsync(AwdCheckAdjudicationCommand command, CancellationToken ct)
    {
        // Optimistic concurrency makes a concurrent callback or second administrator reload the evidence.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var fact = await Query(command.CompetitionId, command.GameplayFactId).SingleOrDefaultAsync(ct);
        if (fact is null) return new(AwdCheckAdjudicationState.NotFound);
        var evidence = fact.Awd!;
        var previousHealth = evidence.Health;
        var previousSettlement = evidence.Settlement;
        var conclusion = AwdHealthEvaluator.ValidateManifest(new(evidence.Connection, evidence.ConnectionReason),
            new(evidence.Functionality, evidence.FunctionalityReason),
            evidence.Flags.Select(flag => new AwdExpectedFlag(flag.FlagId, flag.Historical, flag.Obligation, flag.RequiresWrite)).ToArray(),
            evidence.Flags.Select(flag => new AwdReportedFlag(flag.FlagId,
                new(flag.Write, flag.WriteReason), new(flag.Read, flag.ReadReason))).ToArray()).Conclusion;
        var state = AdjudicateAwdCheck.Apply(fact, command, conclusion);
        if (state != AwdCheckAdjudicationState.Applied) return new(state);
        await events.RecordAsync(new(command.CompetitionId, CompetitionEventKind.GameplayFactAdjudicated,
            CompetitionEventLevel.Warning, CompetitionEventVisibility.Staff, command.At,
            ActorUserId: command.ActorUserId, TeamId: fact.TeamId, CompetitionChallengeId: fact.CompetitionChallengeId,
            GameplayFactId: fact.Id, GameplayFactKind: fact.Kind, GameplayFactState: fact.State,
            GameplayFactResult: fact.Result, Reason: command.Reason, ResolvedAt: command.At,
            AwdAdjudication: new(command.Action, previousHealth, evidence.Health, previousSettlement, evidence.Settlement)), ct);
        await publisher.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
        await publisher.PublishAsync(new ProjectLeaderboard(fact.CompetitionId));
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            publisher.DiscardPendingMessages();
            db.ChangeTracker.Clear();
            return new(AwdCheckAdjudicationState.Conflict);
        }
        await publisher.FlushCommittedMessagesAsync();
        return new(AwdCheckAdjudicationState.Applied, View(fact));
    }

    private IQueryable<GameplayFact> Query(Guid competitionId, Guid gameplayFactId) =>
        db.GameplayFacts.Include(fact => fact.Awd).ThenInclude(evidence => evidence!.Flags)
            .Where(fact => fact.CompetitionId == competitionId && fact.Id == gameplayFactId
                && fact.Kind == GameplayFactKind.AwdServiceTransition && fact.CompetitionChallengeId != null && fact.Awd != null
                && fact.TeamId != null && fact.ReferenceId != null);

    private static AwdCheckEvidenceView View(GameplayFact fact) => new(fact.Id,
        fact.CompetitionChallengeId!.Value, fact.TeamId!.Value, fact.ReferenceId!.Value, fact.Awd!);
}
