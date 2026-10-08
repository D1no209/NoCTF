using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;
using NoCTF.Application.Messaging;
using NoCTF.GameModes.LiveSolo.Gameplay;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    public async Task ResolveAsync(Guid roundId, DateTimeOffset now, CancellationToken ct)
    {
        var ended = await TransactionAsync(async () =>
        {
            var round = await db.LiveSoloRounds.Include(x => x.Pauses).Include(x => x.Questions).SingleOrDefaultAsync(x => x.Id == roundId, ct);
            if (round is null || round.State is LiveSoloRoundState.Preparing or LiveSoloRoundState.Countdown or LiveSoloRoundState.Canceled or LiveSoloRoundState.TimedOut) return false;
            // A durable outcome is immutable here; corrections require the dedicated adjudication flow.
            if (round.State == LiveSoloRoundState.Won) return true;
            var match = await MatchAsync((await db.LiveSoloMatches.AsNoTracking().Where(x => x.Id == round.MatchId).Select(x => x.CompetitionId).SingleAsync(ct)), round.MatchId, ct);
            if (match is null || match.CurrentRoundId != round.Id) return false;
            await SynchronizeCompetitionPausesAsync(round, match.CompetitionId, now, true, ct);
            var associations = await db.LiveSoloSubmissions.AsNoTracking().Where(x => x.RoundId == round.Id).OrderBy(x => x.AdmissionSequence).ToArrayAsync(ct);
            var facts = await db.GameplayFacts.Include(x => x.AcquisitionEvidence).Where(x => associations.Select(a => a.GameplayFactId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var competition = await db.Competitions.AsNoTracking().SingleAsync(x => x.Id == match.CompetitionId, ct);
            var directory = await db.CompetitionChallenges.AsNoTracking().Where(x => round.Questions.Select(q => q.CompetitionChallengeId).Contains(x.Id))
                .Join(db.Challenges.AsNoTracking(), x => x.ChallengeId, x => x.Id, (entry, template) => new { Entry = entry, Template = template }).ToDictionaryAsync(x => x.Entry.Id, ct);
            var questionIds = round.Questions.Select(x => x.Id).ToArray();
            var runtimes = await db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes().Where(x => x.ExecutionScopeId != null
                    && questionIds.Contains(x.ExecutionScopeId.Value)).Select(x => new { x.Id, x.ExecutionScopeId, x.RunningAt }).ToArrayAsync(ct);
            var assignments = await db.LiveSoloAttachmentAssignments.AsNoTracking().Where(x => questionIds.Contains(x.RoundQuestionId)).ToArrayAsync(ct);
            var flags = await db.ChallengeFlags.AsNoTracking().Where(x => x.DeletedAt == null
                && (directory.Values.Select(d => d.Template.Id).Contains(x.ChallengeId ?? Guid.Empty)
                    || directory.Keys.Contains(x.CompetitionChallengeId ?? Guid.Empty))).ToArrayAsync(ct);
            var judged = new List<LiveSoloOrderedResult>(); var evaluator = new LiveSoloFlagEvaluator();
            foreach (var association in associations)
            {
                var fact = facts[association.GameplayFactId];
                if (round.State == LiveSoloRoundState.Won)
                {
                    if (fact.State != GameplayFactState.Completed) { fact.State = GameplayFactState.Completed; fact.Result = GameplayFactResult.Rejected; fact.FailureCode = GameplayFactFailureCode.RoundOutOfRange; fact.UpdatedAt = now; }
                    continue;
                }
                if (fact.State is GameplayFactState.Queued or GameplayFactState.Pending or GameplayFactState.Processing)
                {
                    var question = round.Questions.Single(x => x.Id == association.RoundQuestionId);
                    var challenge = directory[question.CompetitionChallengeId];
                    var applicable = flags.Where(flag => flag.SpecificationKind switch
                    {
                        // Historical Runtime UUIDs remain eligible only for their original scope and validity interval.
                        SpecificationKind.RuntimeInstance => runtimes.Any(r => r.ExecutionScopeId == question.Id && r.Id == flag.SpecificationId
                            && r.RunningAt != null && r.RunningAt <= fact.OccurredAt),
                        SpecificationKind.Attachment => flag.ChallengeId == challenge.Template.Id && flag.TeamId == null,
                        null => flag.TeamId is null && (flag.ChallengeId == challenge.Template.Id || flag.CompetitionChallengeId == challenge.Entry.Id),
                        _ => false
                    }).Where(flag => flag.ChallengeId == challenge.Template.Id || flag.CompetitionChallengeId == challenge.Entry.Id).ToArray();
                    var assignedFlags = LiveSoloAttachmentFlagPolicy.Bind(applicable, assignments, question.Id, challenge.Entry.Id, fact.TeamId!.Value, fact.OccurredAt);
                    var decision = evaluator.Evaluate(new(fact, associations.Where(a => a.RoundQuestionId == question.Id && a.AdmissionSequence < association.AdmissionSequence)
                        .Select(a => facts[a.GameplayFactId]).ToArray(), assignedFlags, null, competition.ModeConfiguration!, challenge.Entry.Rules!,
                        ChallengeDefinition: challenge.Template.Definition));
                    fact.Result = decision.Result; fact.FailureCode = decision.FailureCode; fact.VictimTeamId = decision.VictimTeamId;
                    fact.State = decision.Result is null ? GameplayFactState.PlatformFailed : GameplayFactState.Completed; fact.UpdatedAt = now;
                    await messages.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
                    if (fact.TeamId is Guid sourceTeam && fact.ActorUserId is Guid actor && CheatIncidentFailures.IsIncident(fact.FailureCode))
                    {
                        if (fact.FailureCode == GameplayFactFailureCode.ForeignTeamFlagDetected && fact.VictimTeamId is Guid owner)
                            await messages.PublishAsync(new ForeignTeamFlagDetected(fact.CompetitionId, fact.Id, sourceTeam, owner, actor,
                                fact.CompetitionChallengeId, fact.OccurredAt));
                        else if (fact.FailureCode != GameplayFactFailureCode.ForeignTeamFlagDetected)
                            await messages.PublishAsync(new StaticFlagAcquisitionViolationDetected(fact.CompetitionId, fact.Id, sourceTeam, actor,
                                fact.CompetitionChallengeId, fact.FailureCode!.Value, fact.OccurredAt));
                    }
                }
                judged.Add(new(association.AdmissionSequence, fact.Id, fact.TeamId!.Value, fact.State, fact.Result));
                var outcome = LiveSoloRoundRules.Resolve(round.LastAdmissionSequence, judged);
                if (outcome.State == LiveSoloResolution.Winner) LiveSoloRoundRules.ApplyWinner(match, round, outcome, now);
            }
            if (round.State != LiveSoloRoundState.Won)
            {
                var outcome = LiveSoloRoundRules.Resolve(round.LastAdmissionSequence, judged);
                round.LastResolvedSequence = outcome.ResolvedThrough;
                if (LiveSoloRoundRules.TryVoid(round, outcome, now)) match.State = LiveSoloMatchState.Preparing;
            }
            if (match.State == LiveSoloMatchState.Completed)
            {
                await db.LiveSoloActiveTeamSlots.Where(x => x.MatchId == match.Id).ExecuteDeleteAsync(ct);
                foreach (var slot in db.ChangeTracker.Entries<LiveSoloActiveTeamSlot>().Where(x => x.Entity.MatchId == match.Id).ToArray())
                    slot.State = EntityState.Detached;
                await AdvanceBracketAsync(match, now, ct);
            }
            await db.SaveChangesAsync(ct);
            return round.State is LiveSoloRoundState.Won or LiveSoloRoundState.TimedOut;
        }, () => false, ct);
        if (ended) await runtimePreparation.StopRoundAsync(roundId, now, ct);
    }

    public async Task TickAsync(Guid roundId, long timelineRevision, DateTimeOffset now, CancellationToken ct)
    {
        var roundInfo = await db.LiveSoloRounds.AsNoTracking().SingleOrDefaultAsync(x => x.Id == roundId, ct);
        if (roundInfo is null || roundInfo.TimelineRevision != timelineRevision) return;
        if (roundInfo.State is LiveSoloRoundState.Won or LiveSoloRoundState.TimedOut or LiveSoloRoundState.Canceled)
        { await runtimePreparation.StopRoundAsync(roundId, now, ct); return; }
        foreach (var question in await db.LiveSoloRoundQuestions.AsNoTracking().Where(x => x.RoundId == roundId && x.OpenedAt == null).OrderBy(x => x.Position).Select(x => x.Id).ToArrayAsync(ct))
            await runtimePreparation.PrepareAsync(question, now, ct);
        var due = await TransactionAsync(async () =>
        {
            var round = await db.LiveSoloRounds.Include(x => x.Pauses).Include(x => x.Questions).SingleAsync(x => x.Id == roundId, ct);
            var match = await db.LiveSoloMatches.Include(x => x.Slots).Include(x => x.Roster).SingleAsync(x => x.Id == round.MatchId, ct);
            if (round.TimelineRevision != timelineRevision || match.CurrentRoundId != round.Id) return false;
            await SynchronizeCompetitionPausesAsync(round, match.CompetitionId, now, true, ct);
            var competition = await db.Competitions.AsNoTracking().SingleAsync(x => x.Id == match.CompetitionId, ct);
            if (competition.Status != NoCTF.Domain.Competitions.CompetitionStatus.Running || match.State == LiveSoloMatchState.Paused) return false;
            if (round.State == LiveSoloRoundState.Countdown && round.CountdownAt is { } countdown
                && LiveSoloActiveClock.Elapsed(countdown, now, round.Pauses) >= TimeSpan.FromSeconds(round.CountdownSeconds)
                && round.Questions.Single(x => x.Position == 0).Readiness == LiveSoloQuestionReadiness.Ready)
            {
                if (competition.ModeConfiguration is not LiveSoloCompetitionModeConfiguration { Enabled: true }
                    || !await RosterEligibleAsync(match, ct))
                { round.State = LiveSoloRoundState.Canceled; match.State = LiveSoloMatchState.AwaitingAdjudication; round.TimelineRevision++; return true; }
                if (match.StartedAt is null && !await NoNewPublicExposureAsync(match, round, now, ct))
                { round.State = LiveSoloRoundState.Canceled; match.State = LiveSoloMatchState.Preparing; round.TimelineRevision++; return true; }
                round.State = LiveSoloRoundState.Running; round.StartedAt = now; match.State = LiveSoloMatchState.Running;
                match.StartedAt ??= now; round.Questions.Single(x => x.Position == 0).OpenedAt = now; round.TimelineRevision++;
            }
            if (round.State == LiveSoloRoundState.Running)
            {
                foreach (var question in round.Questions.OrderBy(x => x.Position))
                    if (LiveSoloRoundRules.CanRelease(round, question, now)) { question.OpenedAt = now; round.TimelineRevision++; }
                if (round.StartedAt is { } started && LiveSoloActiveClock.Elapsed(started, now, round.Pauses) >= TimeSpan.FromSeconds(round.LimitSeconds))
                { round.State = LiveSoloRoundState.ConfirmingResult; round.TimelineRevision++; return true; }
            }
            if (round.State == LiveSoloRoundState.Running && round.TimelineRevision != timelineRevision && round.StartedAt is { } activeStart)
            {
                var elapsed = LiveSoloActiveClock.Elapsed(activeStart, now, round.Pauses).TotalSeconds;
                var nextOffset = round.Questions.Where(x => x.OpenedAt == null).Select(x => x.OpenOffsetSeconds)
                    .Append(round.LimitSeconds).Min();
                var next = now.AddSeconds(Math.Max(0.5, nextOffset - elapsed));
                await messages.ScheduleAsync(new AdvanceLiveSoloRound(round.Id, round.TimelineRevision, next), next);
            }
            await db.SaveChangesAsync(ct); return false;
        }, () => false, ct);
        if (due)
        {
            if (await db.LiveSoloRounds.AnyAsync(x => x.Id == roundId && x.State == LiveSoloRoundState.Canceled, ct))
                await runtimePreparation.StopRoundAsync(roundId, now, ct);
            else await ResolveAsync(roundId, now, ct);
        }
    }
}
