using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Challenges.Actions;
using NoCTF.Domain.Commands;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    public async Task<LiveSoloAdmissionResult> AdmitAsync(LiveSoloAdmission command, CancellationToken ct) => await TransactionAsync(async () =>
    {
        var team = await TeamAsync(command.CompetitionId, command.ActorId, ct);
        if (team is not Guid teamId || !await ActiveAsync(command.ActorId, ct)) return new LiveSoloAdmissionResult(null, null, null, LiveSoloFailure.Forbidden);
        if (replay is not null)
        {
            var previous = await replay.FindAsync<GameplayFactAcceptanceResult[]>(new(command.ActorId, ReplayOperation.FlagSubmission,
                command.CompetitionId, command.RoundId), new FlagReplayFingerprint([command.QuestionId.ToString("N"), command.Flag]), ct);
            if (previous?.SingleOrDefault()?.GameplayFactId is Guid previousId)
            {
                var association = await db.LiveSoloSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.GameplayFactId == previousId
                    && x.RoundId == command.RoundId && x.RoundQuestionId == command.QuestionId
                    && db.LiveSoloRounds.Any(r => r.Id == x.RoundId && r.MatchId == command.MatchId)
                    && db.LiveSoloMatches.Any(m => m.Id == command.MatchId && m.CompetitionId == command.CompetitionId), ct);
                if (association is null) return new(null, null, null, LiveSoloFailure.NotFound);
                return new(previousId, association.AdmissionSequence, previous[0].OccurredAt);
            }
        }
        var match = await MatchAsync(command.CompetitionId, command.MatchId, ct);
        if (match is null || !match.Roster.Any(x => x.TeamId == teamId && x.UserId == command.ActorId)
            || !match.Slots.Any(x => x.TeamId == teamId && x.RosterLockedAt != null)) return new(null, null, null, LiveSoloFailure.Forbidden);
        if (match.State == LiveSoloMatchState.Paused) return new(null, null, null, LiveSoloFailure.RoundPaused);
        if (match.PendingCorrectionId is not null || match.SupersededAt is not null) return new(null, null, null, LiveSoloFailure.RoundNotRunning);
        if (match.State != LiveSoloMatchState.Running || match.CurrentRoundId != command.RoundId) return new(null, null, null, LiveSoloFailure.RoundNotRunning);
        var competition = await db.Competitions.AsNoTracking().SingleAsync(x => x.Id == command.CompetitionId, ct);
        if (competition.ModeConfiguration is not LiveSoloCompetitionModeConfiguration { Enabled: true }) return new(null, null, null, LiveSoloFailure.Disabled);
        if (competition.Status == NoCTF.Domain.Competitions.CompetitionStatus.Paused) return new(null, null, null, LiveSoloFailure.RoundPaused);
        if (competition.Status != NoCTF.Domain.Competitions.CompetitionStatus.Running) return new(null, null, null, LiveSoloFailure.RoundNotRunning);
        var round = await db.LiveSoloRounds.Include(x => x.Pauses).Include(x => x.Questions).SingleOrDefaultAsync(x => x.Id == command.RoundId && x.MatchId == match.Id, ct);
        var question = round?.Questions.SingleOrDefault(x => x.Id == command.QuestionId);
        if (round is null || question is null) return new(null, null, null, LiveSoloFailure.NotFound);
        if (!await db.Set<LiveSoloCompetitionChallengeRules>().AnyAsync(rule => rule.CompetitionChallengeId == question.CompetitionChallengeId
                && rule.CompletionKind == LiveSoloCompletionKind.Break, ct)) return new(null, null, null, LiveSoloFailure.SubmissionActionUnsupported);
        await SynchronizeCompetitionPausesAsync(round, command.CompetitionId, (clock ?? TimeProvider.System).GetUtcNow(), true, ct);
        var admittedAt = (clock ?? TimeProvider.System).GetUtcNow();
        if (LiveSoloRoundRules.CanAdmit(round, question, admittedAt) is { } denied) return new(null, null, null, denied);
        var fact = new BreakAttemptGameplayFact { Id = Guid.CreateVersion7(admittedAt), CompetitionId = command.CompetitionId,
            CompetitionChallengeId = question.CompetitionChallengeId, TeamId = teamId, ActorUserId = command.ActorId,
            State = GameplayFactState.Queued, Value = command.Flag, ValueSha256 = ManageChallengeFlags.Hash(command.Flag), OccurredAt = admittedAt, UpdatedAt = admittedAt,
            AcquisitionEvidence = await CaptureEvidenceAsync(question, round.StaticFlagEvidenceRequired, teamId, admittedAt, ct) };
        var sequence = checked(round.LastAdmissionSequence + 1); round.LastAdmissionSequence = sequence;
        db.GameplayFacts.Add(fact); db.LiveSoloSubmissions.Add(new() { GameplayFactId = fact.Id, RoundId = round.Id,
            RoundQuestionId = question.Id, AdmissionSequence = sequence });
        replay?.Store(new[] { new GameplayFactAcceptanceResult(GameplayFactAcceptanceState.Created, fact.Id, admittedAt) });
        await db.SaveChangesAsync(ct);
        await messages.PublishAsync(new NoCTF.Application.Messaging.EvaluateGameplayFact(fact.Id, fact.ConcurrencyStamp));
        return new(fact.Id, sequence, admittedAt);
    }, () => new(null, null, null, LiveSoloFailure.Conflict), ct);

    private async Task<FlagAcquisitionEvidence> CaptureEvidenceAsync(LiveSoloRoundQuestion question, bool required, Guid teamId, DateTimeOffset at, CancellationToken ct)
    {
        if (!required) return new FlagAcquisitionEvidence { CapturedAt = at, Source = FlagAcquisitionEvidenceSource.Recorded };
        var templateId = await db.CompetitionChallenges.Where(x => x.Id == question.CompetitionChallengeId).Select(x => x.ChallengeId).SingleAsync(ct);
        var definition = await db.Set<ChallengeDefinition>().Include(x => x.Runtime).SingleAsync(x => x.ChallengeId == templateId, ct);
        var evidence = new FlagAcquisitionEvidence { CapturedAt = at, Source = FlagAcquisitionEvidenceSource.Recorded };
        var rules = await db.Set<LiveSoloCompetitionChallengeRules>().AsNoTracking()
            .SingleAsync(value => value.CompetitionChallengeId == question.CompetitionChallengeId, ct);
        if (rules.Runtime.FlagSource != PersistedRuntimeFlagSource.Static) return evidence;
        evidence.Scope = FlagAcquisitionScope.FormalStaticExecution;
        if (definition.Runtime is ContainerChallengeRuntimeTemplate) evidence.Required |= FlagAcquisitionResource.Container;
        if (await db.Set<ChallengeAttachment>().AnyAsync(x => x.ChallengeId == templateId && x.DeletedAt == null, ct)) evidence.Required |= FlagAcquisitionResource.Attachment;
        var runtime = await db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes().Where(x => x.ExecutionScopeId == question.Id && x.TeamId == teamId
                && x.RunningAt != null && x.RunningAt <= at && x.Purpose == RuntimePurpose.Player)
            .OrderBy(x => x.RunningAt).ThenBy(x => x.Id).Select(x => new { x.Id, x.RunningAt }).FirstOrDefaultAsync(ct);
        if (runtime is not null) { evidence.Acquired |= FlagAcquisitionResource.Container; evidence.RuntimeInstanceId = runtime.Id; evidence.RuntimeStartedAt = runtime.RunningAt; }
        var download = await db.LiveSoloDownloadEvidences.AsNoTracking().Where(x => x.RoundQuestionId == question.Id)
            .Join(db.GameplayFacts.AsNoTracking(), x => x.GameplayFactId, x => x.Id, (binding, fact) => fact)
            .Where(x => x.TeamId == teamId && x.Kind == GameplayFactKind.AttachmentDownload && x.Result == GameplayFactResult.Applied
                && x.State == GameplayFactState.Completed && x.OccurredAt <= at)
            .OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).Select(x => new { x.Id, x.OccurredAt }).FirstOrDefaultAsync(ct);
        if (download is not null) { evidence.Acquired |= FlagAcquisitionResource.Attachment; evidence.AttachmentDownloadFactId = download.Id; evidence.AttachmentDownloadedAt = download.OccurredAt; }
        return evidence;
    }

    public async Task<IReadOnlyList<LiveSoloQuestionView>?> QuestionsAsync(Guid competitionId, Guid matchId, Guid roundId, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        var team = await TeamAsync(competitionId, actorId, ct);
        var match = await MatchAsync(competitionId, matchId, ct);
        if (!await ActiveAsync(actorId, ct) || match is null || team is not Guid teamId || match.CurrentRoundId != roundId
            || !(match.Roster.Any(x => x.UserId == actorId && x.TeamId == teamId)
                || match.TeammatesMayViewQuestions && match.Slots.Any(x => x.TeamId == teamId))
            || match.State is not (LiveSoloMatchState.Running or LiveSoloMatchState.Paused)) return null;
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == competitionId, ct);
        if (competition?.Status is not (NoCTF.Domain.Competitions.CompetitionStatus.Running or NoCTF.Domain.Competitions.CompetitionStatus.Paused)
            || competition.ModeConfiguration is not LiveSoloCompetitionModeConfiguration { Enabled: true }
            || !await db.LiveSoloRounds.AnyAsync(x => x.Id == roundId && x.MatchId == matchId
                && (x.State == LiveSoloRoundState.Running || x.State == LiveSoloRoundState.ConfirmingResult), ct)) return null;
        var round = await db.LiveSoloRounds.AsNoTracking().Include(value => value.Pauses).SingleAsync(value => value.Id == roundId, ct);
        await SynchronizeCompetitionPausesAsync(round, competitionId, now, false, ct);
        var questions = await db.LiveSoloRoundQuestions.AsNoTracking().Include(x => x.Runtimes).Include(x => x.OpenedContent)
            .Where(x => x.RoundId == roundId && x.OpenedAt != null && x.OpenedAt <= now).OrderBy(x => x.Position).ToArrayAsync(ct);
        var questionIds = questions.Select(x => x.Id).ToArray();
        var runtimes = await db.RuntimeInstances.AsNoTracking().Where(x => x.TeamId == teamId && x.ExecutionScopeId != null
            && questionIds.Contains(x.ExecutionScopeId.Value)).OrderByDescending(x => x.ActiveSlot != null)
            .ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new { x.ExecutionScopeId, x.Id }).ToArrayAsync(ct);
        var current = runtimes.GroupBy(x => x.ExecutionScopeId!.Value).ToDictionary(x => x.Key, x => (Guid?)x.First().Id);
        var challengeIds = questions.Select(value => value.CompetitionChallengeId).ToArray();
        var completions = await db.Set<LiveSoloCompetitionChallengeRules>().AsNoTracking()
            .Where(value => challengeIds.Contains(value.CompetitionChallengeId)).ToDictionaryAsync(value => value.CompetitionChallengeId, value => value.CompletionKind, ct);
        var fixTargets = await db.Set<LiveSoloFixTargetOwnership>().AsNoTracking().Where(value => questionIds.Contains(value.RoundQuestionId))
            .Join(db.RuntimeInstances.AsNoTracking().Where(value => value.TeamId == teamId), value => value.RuntimeInstanceId, value => value.Id,
                (binding, instance) => new { binding.RoundQuestionId, instance.Id, instance.State, instance.GameplayFactId, instance.CreatedAt,
                    Sequence = db.LiveSoloSubmissions.Where(value => value.GameplayFactId == instance.GameplayFactId).Select(value => (long?)value.AdmissionSequence).SingleOrDefault() })
            .OrderByDescending(value => value.GameplayFactId == null
                && (value.State == RuntimeState.Queued || value.State == RuntimeState.Provisioning || value.State == RuntimeState.Running))
            .ThenByDescending(value => value.CreatedAt).ThenByDescending(value => value.Sequence).ThenByDescending(value => value.Id).ToArrayAsync(ct);
        var latestFix = fixTargets.GroupBy(value => value.RoundQuestionId).ToDictionary(value => value.Key, value => value.First());
        LiveSoloAvailableAction[] Actions(LiveSoloRoundQuestion question)
        {
            LiveSoloFailure? failure = match.PendingCorrectionId is not null || match.SupersededAt is not null ? LiveSoloFailure.RoundNotRunning
                : !match.Roster.Any(member => member.UserId == actorId && member.TeamId == teamId)
                    || !match.Slots.Any(slot => slot.TeamId == teamId && slot.RosterLockedAt != null) ? LiveSoloFailure.Forbidden
                : match.State == LiveSoloMatchState.Paused || competition.Status == NoCTF.Domain.Competitions.CompetitionStatus.Paused ? LiveSoloFailure.RoundPaused
                : LiveSoloRoundRules.CanAdmit(round, question, now);
            var availability = failure is null ? ChallengeActionAvailability.Available : ChallengeActionAvailability.AdmissionRejected;
            if (completions[question.CompetitionChallengeId] == LiveSoloCompletionKind.Break)
                return [new(ChallengeActionKind.SubmitFlag, failure is null, availability, failure)];
            var target = latestFix.GetValueOrDefault(question.Id);
            var ready = target is { GameplayFactId: null, State: RuntimeState.Queued or RuntimeState.Provisioning or RuntimeState.Running };
            return [new(ChallengeActionKind.PrepareFixTarget, failure is null && !ready, failure is not null ? availability
                        : ready ? ChallengeActionAvailability.TargetBusy : ChallengeActionAvailability.Available, failure),
                new(ChallengeActionKind.SubmitFix, failure is null && ready, failure is not null ? availability
                        : ready ? ChallengeActionAvailability.Available : ChallengeActionAvailability.TargetRequired,
                    failure, target?.Id, target?.GameplayFactId)];
        }
        return questions.Where(x => x.OpenedContent != null).Select(x => new LiveSoloQuestionView(x.Id, x.CompetitionChallengeId, x.Position,
            x.OpenedContent!.Title, x.OpenedContent.Description,
            x.OpenedContent.Direction, x.OpenedContent.Tags.OrderBy(t => t.Position).Select(t => t.Name).ToArray(),
            x.OpenedAt!.Value, current.GetValueOrDefault(x.Id))
            { CompletionKind = completions[x.CompetitionChallengeId], AvailableActions = Actions(x) }).ToArray();
    }
}
