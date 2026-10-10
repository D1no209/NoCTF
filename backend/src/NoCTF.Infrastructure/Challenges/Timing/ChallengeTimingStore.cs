using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Challenges.Timing;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Competitions.Progression;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Timing;

public sealed class ChallengeTimingStore(NoCtfDbContext db, IPostCommitMessagePublisher messages,
    IChallengeManagementStore challenges, ICompetitionEventRecorder events) : IChallengeTimingStore
{
    public async Task<ChallengeTimingFailure?> ChangeAsync(ChangeChallengeTiming command, CancellationToken ct)
    {
        if (!command.Timing.IsValid) return ChallengeTimingFailure.InvalidOrder;
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, IsolationLevel.Serializable, ct);
        var challenge = await db.CompetitionChallenges.SingleOrDefaultAsync(x => x.Id == command.CompetitionChallengeId
            && x.CompetitionId == command.CompetitionId, ct);
        if (challenge is null) return ChallengeTimingFailure.NotFound;
        if (challenge.Mode == GameMode.LiveSolo) return ChallengeTimingFailure.UnsupportedMode;
        if (challenge.IsPublished && command.Timing.AutoOpenAt > command.Now) return ChallengeTimingFailure.PublishedFutureOpening;
        if (ChallengeTiming.From(challenge) == command.Timing) return null;
        var openingChanged = challenge.AutoOpenAt != command.Timing.AutoOpenAt;
        challenge.AutoOpenAt = command.Timing.AutoOpenAt;
        challenge.ScoringEndsAt = command.Timing.ScoringEndsAt;
        challenge.SubmissionDeadlineAt = command.Timing.SubmissionDeadlineAt;
        if (openingChanged)
            challenge.OpeningState = challenge.AutoOpenAt is null ? ChallengeOpeningState.None
                : challenge.IsPublished ? ChallengeOpeningState.Applied : ChallengeOpeningState.Pending;
        challenge.TimingRevision = Guid.NewGuid();
        challenge.UpdatedAt = command.Now;
        await messages.PublishAsync(new RecalculateChallengeTiming(challenge.Id, challenge.TimingRevision));
        if (challenge.OpeningState == ChallengeOpeningState.Pending)
            await messages.PublishAsync(new AdvanceChallengeOpening(challenge.Id, challenge.TimingRevision));
        await RecordAsync(challenge, command.Now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(messages);
        return null;
    }

    public Task OpenAsync(AdvanceChallengeOpening command, DateTimeOffset now, CancellationToken ct) => RetryAsync(async () =>
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, IsolationLevel.Serializable, ct);
        var challenge = await db.CompetitionChallenges.SingleOrDefaultAsync(x => x.Id == command.CompetitionChallengeId, ct);
        if (challenge is null || challenge.Mode == GameMode.LiveSolo || challenge.TimingRevision != command.TimingRevision
            || challenge.OpeningState != ChallengeOpeningState.Pending || challenge.AutoOpenAt is null || challenge.AutoOpenAt > now) return;
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == challenge.CompetitionId, ct);
        if (competition is null || competition.DeletedAt is not null
            || !(competition.Status == CompetitionStatus.Running || competition.Mode == GameMode.Ctf
                && competition.Status == CompetitionStatus.Finished && competition.PracticeModeEnabled)) return;
        var published = await challenges.UpdateAsync(new(challenge.CompetitionId, challenge.Id, challenge.Order, true, now,
            challenge.CustomTitle, challenge.DirectionId), ct);
        if (published.Failure is not null) return;
        challenge.OpeningState = ChallengeOpeningState.Applied;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(messages);
    }, ct);

    public Task RecalculateAsync(RecalculateChallengeTiming command, DateTimeOffset now, CancellationToken ct) => RetryAsync(async () =>
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, IsolationLevel.Serializable, ct);
        var challenge = await db.CompetitionChallenges.SingleOrDefaultAsync(x => x.Id == command.CompetitionChallengeId, ct);
        if (challenge is null || challenge.Mode == GameMode.LiveSolo || challenge.TimingRevision != command.TimingRevision) return;
        var competition = await db.Competitions.AsNoTracking().SingleAsync(x => x.Id == challenge.CompetitionId, ct);
        var officialWindow = await NoCTF.Infrastructure.Competitions.Lifecycle.CompetitionOfficialWindowReader.ReadAsync(
            db, competition.Id, competition.StartAt, competition.EndAt, ct);
        var timing = ChallengeTiming.From(challenge);
        var facts = await db.GameplayFacts.Where(x => x.CompetitionChallengeId == challenge.Id
            && x.State == GameplayFactState.Completed && x.Result != null && x.AppliedTimingRevision != challenge.TimingRevision
            && (x.Kind == GameplayFactKind.FlagAttempt || x.Kind == GameplayFactKind.BreakAttempt || x.Kind == GameplayFactKind.FixAttempt))
            .OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).Take(200).ToArrayAsync(ct);
        foreach (var fact in facts)
        {
            var practice = competition.Mode == GameMode.Ctf && fact.Kind == GameplayFactKind.FlagAttempt && fact.OccurredAt >= officialWindow.EndAt;
            fact.TimeEligibility = timing.Eligibility(fact.OccurredAt, practice);
            fact.Result = timing.Classify(fact.Result!.Value, fact.OccurredAt, practice);
            fact.AppliedTimingRevision = challenge.TimingRevision;
            fact.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        var graph = await db.CompetitionProgressions.Include(x => x.Nodes).Include(x => x.Edges).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.CompetitionId == competition.Id, ct);
        if (graph is not null)
            await new ProgressionReconciler(db).ReconcileTeamsAsync(competition.Id,
                facts.Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).Distinct().ToArray(), graph, now, ct);
        var remaining = await db.GameplayFacts.AnyAsync(x => x.CompetitionChallengeId == challenge.Id
            && x.State == GameplayFactState.Completed && x.Result != null && x.AppliedTimingRevision != challenge.TimingRevision
            && (x.Kind == GameplayFactKind.FlagAttempt || x.Kind == GameplayFactKind.BreakAttempt || x.Kind == GameplayFactKind.FixAttempt), ct);
        if (remaining) await messages.PublishAsync(command);
        else challenge.AppliedTimingRevision = challenge.TimingRevision;
        await RecordAsync(challenge, now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(messages);
    }, ct);

    private ValueTask<Guid> RecordAsync(CompetitionChallenge challenge, DateTimeOffset now, CancellationToken ct) => events.RecordAsync(new(
        challenge.CompetitionId, CompetitionEventKind.ChallengeUpdated, CompetitionEventLevel.Information,
        challenge.IsPublished ? CompetitionEventVisibility.Public : CompetitionEventVisibility.Staff,
        now, CompetitionChallengeId: challenge.Id), ct);

    private async Task RetryAsync(Func<Task> work, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { await work(); return; }
            catch (Exception exception) when (attempt < 2 && TransactionFailureClassifier.IsRetryable(exception))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); await Task.Delay(TimeSpan.FromMilliseconds(20 * (attempt + 1)), ct); }
        }
    }
}
