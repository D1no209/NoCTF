using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    public async Task<LiveSoloRoundResult> PrepareRoundAsync(PrepareLiveSoloRound command, CancellationToken ct)
    {
        var result = await TransactionAsync(async () =>
        {
            if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanJudgeAsync(command.ActorId, command.CompetitionId, ct)) return new LiveSoloRoundResult(null, LiveSoloFailure.Forbidden);
            var match = await MatchAsync(command.CompetitionId, command.MatchId, ct);
            if (match is null) return new(null, LiveSoloFailure.NotFound);
            if (await db.Competitions.AnyAsync(x => x.Id == command.CompetitionId && x.Status == CompetitionStatus.Finished, ct))
                return new(null, LiveSoloFailure.AlreadyEnded);
            if (match.PendingCorrectionId is not null) return new(null, LiveSoloFailure.NotReady);
            if (match.ConcurrencyStamp != command.ExpectedStamp) return new(null, LiveSoloFailure.Conflict);
            if (match.State != LiveSoloMatchState.Preparing || match.Slots.Any(x => x.TeamId is null || x.RosterLockedAt is null)) return new(null, LiveSoloFailure.NotReady);
            var config = await db.Set<LiveSoloCompetitionModeConfiguration>().AsNoTracking().SingleAsync(x => x.CompetitionId == command.CompetitionId, ct);
            if (!config.Enabled) return new(null, LiveSoloFailure.Disabled);
            var rounds = await db.LiveSoloRounds.Where(x => x.MatchId == match.Id).OrderBy(x => x.Number).ThenBy(x => x.Replay).ToArrayAsync(ct);
            if (rounds.Any(x => x.State is LiveSoloRoundState.Preparing or LiveSoloRoundState.Countdown or LiveSoloRoundState.Running or LiveSoloRoundState.ConfirmingResult))
                return new(null, LiveSoloFailure.AlreadyEnded);
            var candidates = await QuestionCandidatesAsync(match, command.Now, ct);
            var choice = LiveSoloQuestionSelection.Select(candidates, command.GroupId);
            if (command.GroupId is Guid requested)
            {
                var candidate = candidates.SingleOrDefault(x => x.Id == requested);
                if (candidate is null) return new(null, LiveSoloFailure.QuestionGroupNotFound);
                if (command.ExpectedGroupStamp != candidate.ConcurrencyStamp || command.SelectionRevision != candidate.SelectionRevision) return new(null, LiveSoloFailure.SelectionChanged);
                if (!candidate.Available) return new(null, candidate.Exclusions.Contains(LiveSoloQuestionExclusion.ActiveRoundConflict) ? LiveSoloFailure.ActiveQuestionConflict : LiveSoloFailure.InvalidConfiguration);
            }
            if (choice is null) return new(null, LiveSoloFailure.NoSuitableQuestionGroup);
            var group = await db.LiveSoloQuestionGroups.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == choice.Id && x.CompetitionId == match.CompetitionId, ct);
            var entryIds = group.Items.Select(x => x.CompetitionChallengeId).ToArray();
            var templateIds = await db.CompetitionChallenges.Where(x => entryIds.Contains(x.Id)).Select(x => x.ChallengeId).ToArrayAsync(ct);
            var environments = await db.CompetitionChallenges.Where(x => entryIds.Contains(x.Id)).Join(db.Challenges.AsNoTracking(), x => x.ChallengeId, x => x.Id,
                (entry, template) => new { entry.Id, Required = template.Definition!.Runtime != null }).ToDictionaryAsync(x => x.Id, x => x.Required, ct);
            var runtimeCount = await db.Challenges.CountAsync(x => templateIds.Contains(x.Id) && x.Definition!.Runtime != null, ct);
            var runtimeQuota = await db.Competitions.Where(x => x.Id == command.CompetitionId).Select(x => x.MaxConcurrentRuntimeInstancesPerTeam).SingleAsync(ct);
            if (!LiveSoloRoundSchedulePolicy.FitsRuntimeQuota(runtimeCount, runtimeQuota)) return new(null, LiveSoloFailure.ResourceCapacityExceeded);
            _ = await db.Challenges.Where(x => templateIds.Contains(x.Id)).Select(x => new { x.Id, x.ConcurrencyStamp }).ToArrayAsync(ct);
            var last = rounds.LastOrDefault();
            var replay = last?.State is LiveSoloRoundState.TimedOut or LiveSoloRoundState.Canceled;
            var round = new LiveSoloRound { Id = Guid.CreateVersion7(command.Now), MatchId = match.Id,
                Number = replay ? last!.Number : (last?.Number ?? 0) + 1, Replay = replay ? last!.Replay + 1 : 0,
                QuestionGroupId = group.Id, QuestionGroupStamp = choice.ConcurrencyStamp, SelectionRevision = choice.SelectionRevision,
                SelectionKind = command.GroupId is null ? LiveSoloQuestionSelectionKind.Automatic : LiveSoloQuestionSelectionKind.StaffSpecified,
                SelectedByUserId = command.ActorId,
                SelectionAudits = [new() { Position = 0, Kind = command.GroupId is null ? LiveSoloQuestionSelectionKind.Automatic : LiveSoloQuestionSelectionKind.StaffSpecified,
                    ActorUserId = command.ActorId, QuestionGroupId = group.Id, QuestionGroupStamp = choice.ConcurrencyStamp, SelectionRevision = choice.SelectionRevision, OccurredAt = command.Now }],
                SelectionRisks = choice.Risks.Select((x, position) => new LiveSoloRoundSelectionRisk { Position = position, Kind = x.Kind, CompetitionChallengeId = x.CompetitionChallengeId, CanonicalChallengeId = x.CanonicalChallengeId }).ToList(),
                LimitSeconds = group.RoundLimitSeconds ?? config.RoundLimitSeconds,
                StaticFlagEvidenceRequired = config.StaticFlagEvidenceRequired, PlatformStreamingEnabled = config.PlatformStreamingEnabled, CountdownSeconds = config.CountdownSeconds, CreatedAt = command.Now, State = LiveSoloRoundState.Preparing,
                Questions = group.Items.OrderBy(x => x.Position).Select(x => new LiveSoloRoundQuestion { Id = Guid.CreateVersion7(),
                    CompetitionChallengeId = x.CompetitionChallengeId, EnvironmentRequired = environments[x.CompetitionChallengeId], Position = x.Position,
                    OpenOffsetSeconds = x.OpenOffsetSeconds ?? checked(x.Position * config.QuestionIntervalSeconds) }).ToList() };
            db.LiveSoloRounds.Add(round); match.CurrentRoundId = round.Id;
            if (!round.PlatformStreamingEnabled && match.CurrentMediaSessionId is Guid sessionId)
            {
                var previous = await db.LiveSoloMediaSessions.SingleAsync(x => x.Id == sessionId, ct);
                previous.State = LiveSoloMediaState.Stopping;
                match.CurrentMediaSessionId = null;
                foreach (var grant in await db.LiveSoloMediaGrants.Where(x => x.MediaSessionId == sessionId && x.RevokedAt == null).ToArrayAsync(ct))
                    grant.RevokedAt = command.Now;
                await messages.PublishAsync(new RefreshLiveSoloMedia(previous.Id, previous.RoomIdentity));
            }
            foreach (var slot in match.Slots) slot.ReadyConfirmedAt = null;
            await db.SaveChangesAsync(ct); return new(Round(round, command.Now));
        }, () => new(null, LiveSoloFailure.Conflict), ct);
        if (result.Round is { } prepared)
            foreach (var question in await db.LiveSoloRoundQuestions.Where(x => x.RoundId == prepared.Id && x.Position == 0).Select(x => x.Id).ToArrayAsync(ct))
                await runtimePreparation.PrepareAsync(question, command.Now, ct);
        if (result.Round is { } completed)
        {
            var refreshed = await db.LiveSoloRounds.Include(x => x.Questions).Include(x => x.Pauses).SingleAsync(x => x.Id == completed.Id, ct);
            return new(Round(refreshed, command.Now));
        }
        return result;
    }

    public Task<LiveSoloRoundResult> SpecifyPreparedGroupAsync(SpecifyPreparedLiveSoloGroup command, CancellationToken ct) => TransactionAsync(async () =>
    {
        if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanJudgeAsync(command.ActorId, command.CompetitionId, ct)) return new LiveSoloRoundResult(null, LiveSoloFailure.Forbidden);
        var match = await MatchAsync(command.CompetitionId, command.MatchId, ct);
        var round = await db.LiveSoloRounds.Include(x => x.Questions).Include(x => x.Pauses).Include(x => x.SelectionRisks).Include(x => x.SelectionAudits)
            .SingleOrDefaultAsync(x => x.Id == command.RoundId && x.MatchId == command.MatchId, ct);
        if (match is null || round is null || match.CurrentRoundId != round.Id) return new(null, LiveSoloFailure.NotFound);
        if (round.ConcurrencyStamp != command.ExpectedStamp) return new(null, LiveSoloFailure.Conflict);
        if (match.State != LiveSoloMatchState.Preparing || round.State != LiveSoloRoundState.Preparing || match.PendingCorrectionId != null) return new(null, LiveSoloFailure.NotReady);
        if (command.QuestionGroupId != round.QuestionGroupId) return new(null, LiveSoloFailure.SelectionChanged);
        var candidate = (await QuestionCandidatesAsync(match, command.Now, ct)).SingleOrDefault(x => x.Id == command.QuestionGroupId);
        if (candidate is null) return new(null, LiveSoloFailure.QuestionGroupNotFound);
        if (candidate.ConcurrencyStamp != command.ExpectedGroupStamp || candidate.SelectionRevision != command.SelectionRevision) return new(null, LiveSoloFailure.SelectionChanged);
        if (!candidate.Available) return new(null, LiveSoloFailure.InvalidConfiguration);
        if (round.QuestionGroupStamp is Guid frozenStamp && frozenStamp != candidate.ConcurrencyStamp) return new(null, LiveSoloFailure.SelectionChanged);
        var position = round.SelectionAudits.Count == 0 ? 0 : round.SelectionAudits.Max(x => x.Position) + 1;
        var audit = new LiveSoloRoundSelectionAudit { RoundId = round.Id, Position = position, Kind = LiveSoloQuestionSelectionKind.StaffSpecified,
            ActorUserId = command.ActorId, QuestionGroupId = candidate.Id, QuestionGroupStamp = candidate.ConcurrencyStamp, SelectionRevision = candidate.SelectionRevision, OccurredAt = command.Now };
        round.SelectionAudits.Add(audit); db.Add(audit);
        var riskPosition = round.SelectionRisks.Count == 0 ? 0 : round.SelectionRisks.Max(x => x.Position) + 1;
        foreach (var risk in candidate.Risks)
        {
            var record = new LiveSoloRoundSelectionRisk { RoundId = round.Id, Position = riskPosition++, SelectionPosition = position, Kind = risk.Kind,
                CompetitionChallengeId = risk.CompetitionChallengeId, CanonicalChallengeId = risk.CanonicalChallengeId };
            round.SelectionRisks.Add(record); db.Add(record);
        }
        round.ConcurrencyStamp = Guid.NewGuid();
        round.SelectionKind = LiveSoloQuestionSelectionKind.StaffSpecified; round.SelectedByUserId = command.ActorId;
        round.SelectionRevision = candidate.SelectionRevision; round.QuestionGroupStamp = candidate.ConcurrencyStamp;
        await db.SaveChangesAsync(ct); return new(Round(round, command.Now));
    }, () => new(null, LiveSoloFailure.Conflict), ct);

    public async Task<LiveSoloRoundResult> RetryPreparationAsync(RetryLiveSoloPreparation command, CancellationToken ct)
    {
        Guid? targetQuestion = null;
        var result = await TransactionAsync(async () =>
        {
            if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanJudgeAsync(command.ActorId, command.CompetitionId, ct)) return new LiveSoloRoundResult(null, LiveSoloFailure.Forbidden);
            var match = await MatchAsync(command.CompetitionId, command.MatchId, ct);
            var round = await db.LiveSoloRounds.Include(x => x.Questions).Include(x => x.Pauses).SingleOrDefaultAsync(x => x.Id == command.RoundId && x.MatchId == command.MatchId, ct);
            if (match is null || round is null || match.CurrentRoundId != round.Id) return new(null, LiveSoloFailure.NotFound);
            if (round.ConcurrencyStamp != command.ExpectedStamp) return new(null, LiveSoloFailure.Conflict);
            if (round.State is not (LiveSoloRoundState.Preparing or LiveSoloRoundState.Countdown or LiveSoloRoundState.Running)
                || match.PendingCorrectionId != null || match.SupersededAt != null
                || match.State is LiveSoloMatchState.Completed or LiveSoloMatchState.Canceled) return new(null, LiveSoloFailure.NotReady);
            if (await db.Competitions.AnyAsync(x => x.Id == command.CompetitionId && x.Status == CompetitionStatus.Finished, ct))
                return new(null, LiveSoloFailure.AlreadyEnded);
            var target = command.QuestionId is Guid id ? round.Questions.SingleOrDefault(x => x.Id == id) : round.Questions.Single(x => x.Position == 0);
            if (target is null) return new(null, LiveSoloFailure.NotFound);
            if (target.OpenedAt != null) return new(null, LiveSoloFailure.Conflict);
            if (round.StartedAt is { } start && LiveSoloActiveClock.Elapsed(start, command.Now, round.Pauses) >= TimeSpan.FromSeconds(round.LimitSeconds))
                return new(null, LiveSoloFailure.DeadlinePassed);
            targetQuestion = target.Id; target.Readiness = LiveSoloQuestionReadiness.Waiting; target.PreparationFailure = LiveSoloResourceFailure.None;
            round.TimelineRevision++; round.ConcurrencyStamp = Guid.NewGuid(); await db.SaveChangesAsync(ct); return new(Round(round, command.Now));
        }, () => new(null, LiveSoloFailure.Conflict), ct);
        if (result.Round is null) return result;
        if (targetQuestion is Guid questionId) await runtimePreparation.PrepareAsync(questionId, command.Now, ct);
        return await TransactionAsync(async () =>
        {
            var refreshed = await db.LiveSoloRounds.Include(x => x.Questions).Include(x => x.Pauses).SingleAsync(x => x.Id == result.Round.Id, ct);
            if (LiveSoloRoundSchedulePolicy.NextWakeup(refreshed, command.Now) is { } next)
                await messages.ScheduleAsync(new AdvanceLiveSoloRound(refreshed.Id, refreshed.TimelineRevision, next), next);
            return new LiveSoloRoundResult(Round(refreshed, command.Now));
        }, () => new(null, LiveSoloFailure.Conflict), ct);
    }

    public async Task<LiveSoloRoundResult> StartCountdownAsync(StartLiveSoloCountdown command, CancellationToken ct)
    {
        if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanJudgeAsync(command.ActorId, command.CompetitionId, ct)) return new(null, LiveSoloFailure.Forbidden);
        var target = await db.LiveSoloRounds.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.RoundId && x.MatchId == command.MatchId
            && db.LiveSoloMatches.Any(m => m.Id == x.MatchId && m.CompetitionId == command.CompetitionId), ct);
        if (target is null) return new(null, LiveSoloFailure.NotFound);
        var lifecycle = await db.Competitions.AsNoTracking().Where(x => x.Id == command.CompetitionId).Select(x => (CompetitionStatus?)x.Status).SingleOrDefaultAsync(ct);
        if (lifecycle is CompetitionStatus.Draft or CompetitionStatus.Visible or CompetitionStatus.Published) return new(null, LiveSoloFailure.CompetitionNotStarted);
        if (lifecycle != CompetitionStatus.Running) return new(null, LiveSoloFailure.NotReady);
        if (target.ConcurrencyStamp != command.ExpectedStamp) return new(null, LiveSoloFailure.Conflict);
        if (target.State != LiveSoloRoundState.Preparing) return new(null, LiveSoloFailure.NotReady);
        var preparationMatch = await MatchAsync(command.CompetitionId, command.MatchId, ct);
        if (preparationMatch is null || preparationMatch.CurrentRoundId != target.Id) return new(null, LiveSoloFailure.NotFound);
        if (preparationMatch.State != LiveSoloMatchState.Preparing || preparationMatch.PendingCorrectionId != null || preparationMatch.Slots.Any(x => x.ReadyConfirmedAt == null))
            return new(null, LiveSoloFailure.NotReady);
        var first = await db.LiveSoloRoundQuestions.AsNoTracking().Where(x => x.RoundId == command.RoundId && x.Position == 0).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (first is null) return new(null, LiveSoloFailure.NotFound);
        if (await runtimePreparation.PrepareAsync(first.Value, command.Now, ct) is { } failed) return new(null, failed);
        LiveSoloMediaSession? session = null;
        LiveSoloProgramCapture? program = null;
        LiveSoloRoomObservation? observed = null;
        LiveSoloRecording[] recordingProof = [];
        DateTimeOffset checkedAt = default;
        if (target.PlatformStreamingEnabled)
        {
            var readiness = await media.CheckAsync(ct);
            if (!readiness.Configured || !readiness.Available || !readiness.EgressAvailable) return new(null, LiveSoloFailure.MediaUnavailable);
            session = await db.LiveSoloMediaSessions.AsNoTracking().Include(x => x.Participants)
                .Where(x => x.MatchId == command.MatchId && x.State == LiveSoloMediaState.Ready
                    && db.LiveSoloMatches.Any(match => match.Id == command.MatchId && match.CurrentMediaSessionId == x.Id))
                .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
            if (session is null) return new(null, LiveSoloFailure.MediaUnavailable);
            program = await db.LiveSoloProgramCaptures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == session.CurrentProgramCaptureId
                && x.MediaSessionId == session.Id && x.State == LiveSoloCaptureState.Active, ct);
            if (program?.EgressId is null || !await ProgramReadyAsync(session.Id, program.Id, program.EgressId, ct)) return new(null, LiveSoloFailure.MediaUnavailable);
            observed = await media.ObserveAsync(session.RoomIdentity, ct);
            LiveSoloExportObservation? export;
            try
            {
                var jobs = await egress.ListAsync(session.RoomIdentity, ct);
                export = jobs.SingleOrDefault(x => x.Id == program.EgressId && x.RequestId == program.Id && x.RoomIdentity == session.RoomIdentity);
                if(session.RecordingEnabled)
                {
                    recordingProof=await db.LiveSoloRecordings.AsNoTracking().Where(x=>x.MediaSessionId==session.Id&&x.State==LiveSoloRecordingState.Recording&&x.ReservedBytes>0).ToArrayAsync(ct);
                    foreach(var member in session.Participants)
                        if(!recordingProof.Any(record=>record.UserId==member.UserId&&record.VideoTrackId==member.ScreenTrackId
                            &&jobs.Any(job=>job.Id==record.EgressId&&job.RequestId==record.Id&&job.RoomIdentity==session.RoomIdentity
                                &&job.State==LiveSoloExportState.Active&&job.StartedAt!=null&&job.StartedAt<=(clock??TimeProvider.System).GetUtcNow()&&job.EndedAt==null)))
                            return new(null,LiveSoloFailure.MediaUnavailable);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            { return new(null, LiveSoloFailure.MediaUnavailable); }
            checkedAt = (clock ?? TimeProvider.System).GetUtcNow();
            if (export is not { State: LiveSoloExportState.Active, StartedAt: not null, EndedAt: null }
                || export.StartedAt > checkedAt) return new(null, LiveSoloFailure.MediaUnavailable);
        }
        return await TransactionAsync(async () =>
        {
            var match = await MatchAsync(command.CompetitionId, command.MatchId, ct);
            var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.CompetitionId, ct);
            var round = await db.LiveSoloRounds.Include(x => x.Questions).Include(x => x.Pauses).SingleOrDefaultAsync(x => x.Id == command.RoundId && x.MatchId == command.MatchId, ct);
            if (match is null || round is null || match.CurrentRoundId != round.Id) return new LiveSoloRoundResult(null, LiveSoloFailure.NotFound);
            if (match.PendingCorrectionId is not null) return new(null, LiveSoloFailure.NotReady);
            if (round.ConcurrencyStamp != command.ExpectedStamp) return new(null, LiveSoloFailure.Conflict);
            if (match.State != LiveSoloMatchState.Preparing || round.State != LiveSoloRoundState.Preparing
                || competition?.Status != CompetitionStatus.Running || match.Slots.Any(x => x.ReadyConfirmedAt is null)
                || round.Questions.Single(x => x.Position == 0).Readiness != LiveSoloQuestionReadiness.Ready) return new(null, LiveSoloFailure.NotReady);
            var config = (LiveSoloCompetitionModeConfiguration)competition.ModeConfiguration!;
            if (!config.Enabled) return new(null, LiveSoloFailure.Disabled);
            if (round.PlatformStreamingEnabled != target.PlatformStreamingEnabled) return new(null, LiveSoloFailure.Conflict);
            if (round.PlatformStreamingEnabled)
            {
                if (session is null || program?.EgressId is null || observed is null) return new(null, LiveSoloFailure.MediaUnavailable);
                if (match.CurrentMediaSessionId != session.Id || !await db.LiveSoloMediaSessions.AnyAsync(x => x.Id == session.Id
                    && x.State == LiveSoloMediaState.Ready, ct)) return new(null, LiveSoloFailure.MediaUnavailable);
                if (!observed.Exists || !LiveSoloMediaPolicy.FreshStartProof(checkedAt, (clock ?? TimeProvider.System).GetUtcNow())
                    || !await ProgramReadyAsync(session.Id, program.Id, program.EgressId, ct)) return new(null, LiveSoloFailure.MediaUnavailable);
                if (session.RecordingEnabled)
                {
                    foreach (var member in session.Participants)
                    {
                        var proof = recordingProof.SingleOrDefault(x => x.UserId == member.UserId && x.VideoTrackId == member.ScreenTrackId);
                        if (proof is null || !await db.LiveSoloRecordings.AnyAsync(x => x.Id == proof.Id
                            && x.MediaSessionId == session.Id && x.State == LiveSoloRecordingState.Recording
                            && x.ReservedBytes > 0 && x.EgressId == proof.EgressId
                            && db.Set<LiveSoloMediaParticipant>().Any(p => p.MediaSessionId == session.Id && p.UserId == x.UserId
                                && p.ScreenTrackId == x.VideoTrackId && p.ScreenState == LiveSoloScreenState.Sharing), ct))
                            return new(null, LiveSoloFailure.MediaUnavailable);
                    }
                }
                if (match.Roster.Any(member => !session.Participants.Any(p => p.UserId == member.UserId
                    && observed.Screens.Any(screen => screen.Identity == p.Identity && screen.State == LiveSoloScreenState.Sharing)))) return new(null, LiveSoloFailure.NotReady);
            }
            if (!await RosterEligibleAsync(match, ct)) return new(null, LiveSoloFailure.NotReady);
            if (!await HasMatchCapacityAsync(match, config.MaximumConcurrentMatches, ct))
                return new(null, LiveSoloFailure.NotReady);
            // Exposure can change while both sides are preparing. Recheck before a new Match starts.
            if (round.SelectionKind != LiveSoloQuestionSelectionKind.StaffSpecified && !await NoNewPublicExposureAsync(match, round, ct)) return new(null, LiveSoloFailure.NoSuitableQuestionGroup);
            var effectiveAt = (clock ?? TimeProvider.System).GetUtcNow();
            match.State = LiveSoloMatchState.Countdown; round.State = LiveSoloRoundState.Countdown; round.CountdownAt = effectiveAt; round.TimelineRevision++;
            await messages.ScheduleAsync(new NoCTF.Application.LiveSolo.Rounds.AdvanceLiveSoloRound(round.Id, round.TimelineRevision,
                effectiveAt.AddSeconds(round.CountdownSeconds)), effectiveAt.AddSeconds(round.CountdownSeconds));
            await db.SaveChangesAsync(ct); return new(Round(round, effectiveAt));
        }, () => new(null, LiveSoloFailure.Conflict), ct);
    }

    private async Task<bool> NoNewPublicExposureAsync(LiveSoloMatch match, LiveSoloRound round, CancellationToken ct)
    {
        await NoCTF.Infrastructure.LiveSolo.Questions.LiveSoloPublicExposure.RememberAsync(db, match.CompetitionId, (clock ?? TimeProvider.System).GetUtcNow(), ct);
        var templates = await db.CompetitionChallenges.Where(x => round.Questions.Select(q => q.CompetitionChallengeId).Contains(x.Id)).Select(x => x.ChallengeId).ToArrayAsync(ct);
        var canonical = await db.LiveSoloChallengeSources.Where(x => templates.Contains(x.ChallengeId)).ToDictionaryAsync(x => x.ChallengeId, x => x.CanonicalChallengeId, ct);
        var ids = templates.Select(id => canonical.GetValueOrDefault(id, id)).ToArray();
        var publicTimes = await db.LiveSoloQuestionExposures.Where(x => x.CompetitionId == match.CompetitionId && ids.Contains(x.CanonicalChallengeId))
            .Select(x => x.PublicAt).ToArrayAsync(ct);
        var actualNow = (clock ?? TimeProvider.System).GetUtcNow();
        return !publicTimes.Any(x => x <= actualNow);
    }
    private Task<bool> ProgramReadyAsync(Guid sessionId, Guid captureId, string egressId, CancellationToken ct) => db.LiveSoloProgramCaptures.AnyAsync(x => x.MediaSessionId == sessionId
        && x.Id == captureId && x.EgressId == egressId && x.State == LiveSoloCaptureState.Active && db.LiveSoloMediaSessions.Any(s => s.Id == sessionId && s.CurrentProgramCaptureId == x.Id)
        && x.StalledAt == null
        && db.LiveSoloProgramSegments.Any(segment => segment.ProgramCaptureId == x.Id
            && db.Files.Any(file => file.Id == segment.FileId && file.ByteLength > 0)), ct);

    private async Task<bool> RosterEligibleAsync(LiveSoloMatch match, CancellationToken ct)
    {
        var teamIds = match.Slots.Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).ToArray();
        var members = match.Roster.Select(x => x.UserId).ToArray();
        return teamIds.Length == 2 && members.Length != 0
            && await db.Teams.CountAsync(x => teamIds.Contains(x.Id) && x.CompetitionId == match.CompetitionId
                && !x.IsBanned && x.RegistrationStatus == NoCTF.Domain.Teams.TeamRegistrationStatus.Approved, ct) == 2
            && await db.Users.CountAsync(x => members.Contains(x.Id) && x.AccountStatus == NoCTF.Domain.Identity.UserAccountStatus.Active, ct) == members.Length
            && await db.Set<LiveSoloRosterMember>().Where(x => x.MatchId == match.Id)
                .Join(db.Set<NoCTF.Domain.Teams.TeamMember>(), x => new { x.TeamId, x.UserId }, x => new { x.TeamId, x.UserId }, (roster, _) => roster)
                .CountAsync(ct) == members.Length;
    }
    private static LiveSoloRoundView Round(LiveSoloRound round, DateTimeOffset now) => new(round.Id, round.MatchId, round.Number, round.Replay,
        round.State, round.ConcurrencyStamp, round.TimelineRevision, round.CountdownAt, round.StartedAt, round.LimitSeconds,
        round.StartedAt is { } started ? Math.Min(round.LimitSeconds * 1000L,
            (long)LiveSoloActiveClock.Elapsed(started, round.EndedAt is { } ended && ended < now ? ended : now, round.Pauses).TotalMilliseconds) : 0,
        LiveSoloActiveClock.Paused(round.Pauses), round.WinnerTeamId, round.WinningGameplayFactId, round.PlatformStreamingEnabled)
        { ResultRevision = round.ResultRevision, EndReason = round.EndReason, ResultLeftWins = round.ResultLeftWins,
          ResultRightWins = round.ResultRightWins, EndedAt = round.EndedAt, QuestionGroupId = round.QuestionGroupId, SelectionKind = round.SelectionKind, StaticFlagEvidenceRequired = round.StaticFlagEvidenceRequired,
          ResourceReadiness = round.Questions.OrderBy(x => x.Position).FirstOrDefault()?.Readiness ?? LiveSoloQuestionReadiness.Waiting,
          PreparationFailure = round.Questions.OrderBy(x => x.Position).FirstOrDefault()?.PreparationFailure ?? LiveSoloResourceFailure.None,
          EnvironmentRequired = round.Questions.OrderBy(x => x.Position).FirstOrDefault()?.EnvironmentRequired };

    public async Task<LiveSoloRoundView?> FindRoundAsync(Guid competitionId, Guid matchId, Guid roundId, Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct)
    {
        if (await FindAsync(competitionId, matchId, actorId, staff, now, ct) is null) return null;
        var round = await db.LiveSoloRounds.AsNoTracking().Include(x => x.Pauses).Include(x => x.Questions).SingleOrDefaultAsync(x => x.Id == roundId && x.MatchId == matchId, ct);
        if (round is not null) await SynchronizeCompetitionPausesAsync(round, competitionId, now, false, ct);
        return round is null ? null : Round(round, now);
    }
}
