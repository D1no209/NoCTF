using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Challenges.Capabilities;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore : ILiveSoloFixUploadStore
{
    private sealed record FixAdmissionScope(Guid TeamId, LiveSoloMatch Match, LiveSoloRound Round, LiveSoloRoundQuestion Question,
        RuntimeInstance Runtime, FixSubmissionConfiguration Configuration);

    public async Task<Guid?> ResolveChallengeAsync(LiveSoloResourceRequest request, CancellationToken ct)
    {
        if (!await ActiveAsync(request.ActorId, ct)) return null;
        return await db.LiveSoloRoundQuestions.AsNoTracking().Where(question => question.Id == request.QuestionId && question.RoundId == request.RoundId)
            .Join(db.LiveSoloRounds.AsNoTracking().Where(round => round.MatchId == request.MatchId), question => question.RoundId, round => round.Id, (question, _) => question)
            .Where(question => db.LiveSoloMatches.Any(match => match.Id == request.MatchId && match.CompetitionId == request.CompetitionId))
            .Select(question => (Guid?)question.CompetitionChallengeId).SingleOrDefaultAsync(ct);
    }

    public async Task<LiveSoloAdmissionResult> ReadAcceptanceAsync(LiveSoloResourceRequest request, Guid gameplayFactId, CancellationToken ct)
    {
        var accepted = await db.LiveSoloSubmissions.AsNoTracking().Where(submission => submission.GameplayFactId == gameplayFactId
                && submission.RoundId == request.RoundId && submission.RoundQuestionId == request.QuestionId)
            .Join(db.GameplayFacts.AsNoTracking().Where(fact => fact.ActorUserId == request.ActorId && fact.CompetitionId == request.CompetitionId),
                submission => submission.GameplayFactId, fact => fact.Id, (submission, fact) => new { submission.AdmissionSequence, fact.OccurredAt })
            .SingleOrDefaultAsync(ct);
        if (accepted is null || !await db.LiveSoloRounds.AnyAsync(round => round.Id == request.RoundId && round.MatchId == request.MatchId
                && db.LiveSoloMatches.Any(match => match.Id == round.MatchId && match.CompetitionId == request.CompetitionId), ct))
            return new(null, null, null, LiveSoloFailure.NotFound);
        return new(gameplayFactId, accepted.AdmissionSequence, accepted.OccurredAt);
    }

    private async Task<FixAdmissionScope?> FixAdmissionAsync(LiveSoloResourceRequest request, Guid runtimeId, CancellationToken ct)
    {
        var team = await TeamAsync(request.CompetitionId, request.ActorId, ct);
        if (team is not Guid teamId || !await ActiveAsync(request.ActorId, ct)) return null;
        var tracking = db.Database.CurrentTransaction is not null;
        var matchQuery = db.LiveSoloMatches.Include(value => value.Slots).Include(value => value.Roster)
            .Where(value => value.Id == request.MatchId && value.CompetitionId == request.CompetitionId);
        var match = await (tracking ? matchQuery : matchQuery.AsNoTracking()).SingleOrDefaultAsync(ct);
        if (match is null || match.State != LiveSoloMatchState.Running || match.CurrentRoundId != request.RoundId
            || match.PendingCorrectionId is not null || match.SupersededAt is not null
            || !match.Roster.Any(member => member.TeamId == teamId && member.UserId == request.ActorId)
            || !match.Slots.Any(slot => slot.TeamId == teamId && slot.RosterLockedAt != null)) return null;
        var competition = await db.Competitions.AsNoTracking().AsSplitQuery().SingleAsync(value => value.Id == request.CompetitionId, ct);
        if (competition.Status != CompetitionStatus.Running || competition.Mode != GameMode.LiveSolo
            || competition.ModeConfiguration is not LiveSoloCompetitionModeConfiguration { Enabled: true }) return null;
        var roundQuery = db.LiveSoloRounds.Include(value => value.Pauses).Include(value => value.Questions)
            .Where(value => value.Id == request.RoundId && value.MatchId == match.Id);
        var round = await (tracking ? roundQuery : roundQuery.AsNoTracking()).SingleOrDefaultAsync(ct);
        var question = round?.Questions.SingleOrDefault(value => value.Id == request.QuestionId);
        if (round is null || question is null) return null;
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        await SynchronizeCompetitionPausesAsync(round, request.CompetitionId, now, tracking, ct);
        if (LiveSoloRoundRules.CanAdmit(round, question, now) is not null) return null;
        var side = match.Slots.Single(slot => slot.TeamId == teamId).Side;
        var targetQuery = db.Set<LiveSoloFixTargetOwnership>().Where(ownership => ownership.RoundQuestionId == question.Id && ownership.Side == side)
            .Join(db.RuntimeInstances, ownership => ownership.RuntimeInstanceId, runtime => runtime.Id, (_, runtime) => runtime)
            .Where(runtime => runtime.Id == runtimeId && runtime.CompetitionId == request.CompetitionId
                && runtime.CompetitionChallengeId == question.CompetitionChallengeId && runtime.TeamId == teamId
                && runtime.Purpose == RuntimePurpose.ScopedFixTarget);
        var target = await (tracking ? targetQuery : targetQuery.AsNoTracking()).SingleOrDefaultAsync(ct);
        if (target is null) return null;
        var entry = await db.CompetitionChallenges.AsNoTracking().AsSplitQuery().SingleAsync(value => value.Id == question.CompetitionChallengeId, ct);
        if (entry.Rules is not LiveSoloCompetitionChallengeRules { CompletionKind: LiveSoloCompletionKind.FixAndCheck }) return null;
        var template = await db.Challenges.AsNoTracking().AsSplitQuery().SingleAsync(value => value.Id == entry.ChallengeId, ct);
        var configuration = FixSubmissionConfigurationResolver.Resolve(GameMode.LiveSolo, competition.ModeConfiguration, entry.Rules!, template.Definition!);
        return configuration is null ? null : new(teamId, match, round, question, target, configuration);
    }

    Task<bool> IPatchUploadStore.CanAccessTargetAsync(Guid competitionId, Guid challengeId, Guid targetId, Guid userId, CancellationToken ct) =>
        CanAccessFixTargetAsync(competitionId, challengeId, targetId, userId, ct);

    private async Task<bool> CanAccessFixTargetAsync(Guid competitionId, Guid challengeId, Guid targetId, Guid userId, CancellationToken ct) =>
        await ((IPatchUploadStore)this).ResolveScopeAsync(competitionId, challengeId, targetId, userId, ct) is not null;

    async Task<PatchUploadScope?> IPatchUploadStore.ResolveScopeAsync(Guid competitionId, Guid competitionChallengeId,
        Guid runtimeInstanceId, Guid userId, CancellationToken ct)
    {
        var identity = await db.Set<LiveSoloFixTargetOwnership>().AsNoTracking().Where(ownership => ownership.RuntimeInstanceId == runtimeInstanceId)
            .Join(db.LiveSoloRoundQuestions.AsNoTracking(), ownership => ownership.RoundQuestionId, question => question.Id, (_, question) => question)
            .Where(question => question.CompetitionChallengeId == competitionChallengeId)
            .Join(db.LiveSoloRounds.AsNoTracking(), question => question.RoundId, round => round.Id, (question, round) => new { QuestionId = question.Id, round.Id, round.MatchId })
            .SingleOrDefaultAsync(ct);
        if (identity is null) return null;
        var scope = await FixAdmissionAsync(new(competitionId, identity.MatchId, identity.Id, identity.QuestionId, userId,
            (clock ?? TimeProvider.System).GetUtcNow()), runtimeInstanceId, ct);
        return scope is null ? null : new(competitionId, competitionChallengeId, scope.TeamId, userId, runtimeInstanceId,
            scope.Configuration.MaximumPatchUploadBytes, RuntimePurpose.ScopedFixTarget, FixTargetKind.Isolated, scope.Question.Id);
    }

    Task<bool> IPatchUploadStore.IsTargetUnconsumedAsync(PatchUploadScope scope, CancellationToken ct) =>
        db.RuntimeInstances.AsNoTracking().AnyAsync(runtime => runtime.Id == scope.RuntimeInstanceId && runtime.GameplayFactId == null
            && runtime.TeamId == scope.TeamId && runtime.CompetitionId == scope.CompetitionId && runtime.CompetitionChallengeId == scope.CompetitionChallengeId
            && runtime.Purpose == RuntimePurpose.ScopedFixTarget
            && (runtime.State == RuntimeState.Queued || runtime.State == RuntimeState.Provisioning || runtime.State == RuntimeState.Running), ct);

    Task<PatchUploadSaveResult> IPatchUploadStore.SaveAsync(Guid patchUploadId, Guid gameplayFactId, PatchUploadScope scope,
        Guid fileId, DateTimeOffset uploadedAt, CancellationToken ct) => TransactionAsync(async () =>
    {
        _ = uploadedAt;
        if (scope.ExecutionScopeId is not Guid questionId || !await new FileReferenceLock().AcquireAsync(db, fileId, ct))
            return new PatchUploadSaveResult(PatchUploadSaveState.DefenseTargetNotReady);
        var identity = await db.LiveSoloRoundQuestions.Where(question => question.Id == questionId)
            .Join(db.LiveSoloRounds, question => question.RoundId, round => round.Id, (question, round) => new { RoundId = round.Id, round.MatchId })
            .SingleAsync(ct);
        var request = new LiveSoloResourceRequest(scope.CompetitionId, identity.MatchId, identity.RoundId, questionId, scope.UserId,
            (clock ?? TimeProvider.System).GetUtcNow());
        var admission = await FixAdmissionAsync(request, scope.RuntimeInstanceId, ct);
        if (admission is null) return new(PatchUploadSaveState.DefenseTargetNotReady);
        var target = admission.Runtime;
        if (target.GameplayFactId is not null || await db.PatchUploads.AnyAsync(upload => upload.RuntimeInstanceId == target.Id, ct))
            return new(PatchUploadSaveState.DefenseTargetConsumed);
        if (target.State is not (RuntimeState.Queued or RuntimeState.Provisioning or RuntimeState.Running)) return new(PatchUploadSaveState.DefenseTargetNotReady);
        var admittedAt = (clock ?? TimeProvider.System).GetUtcNow();
        if (LiveSoloRoundRules.CanAdmit(admission.Round, admission.Question, admittedAt) is not null || target.ExpiresAt <= admittedAt)
            return new(PatchUploadSaveState.ChallengeSubmissionClosed);
        var bytes = await db.Files.Where(file => file.Id == fileId).Select(file => file.ByteLength).SingleAsync(ct);
        if (bytes > admission.Configuration.MaximumPatchUploadBytes) return new(PatchUploadSaveState.DefenseTargetNotReady);
        var fact = new FixAttemptGameplayFact { Id = gameplayFactId, CompetitionId = scope.CompetitionId,
            CompetitionChallengeId = admission.Question.CompetitionChallengeId, TeamId = admission.TeamId, ActorUserId = scope.UserId,
            ReferenceKind = GameplayFactReferenceKind.PatchUpload, ReferenceId = patchUploadId, State = GameplayFactState.Pending,
            OccurredAt = admittedAt, UpdatedAt = admittedAt };
        var deadline = NoCTF.Application.Challenges.Capabilities.FixExecutionBudget.CalculateDeadline(admittedAt, null,
            admission.Configuration.PatchTimeoutSeconds, admission.Configuration.Checker.TimeoutSeconds);
        deadline = deadline.AddTicks(-(deadline.Ticks % TimeSpan.TicksPerMicrosecond));
        fact.FixExecution = new() { GameplayFactId = fact.Id, RuntimeInstanceId = target.Id, TargetKind = FixTargetKind.Isolated,
            Phase = FixExecutionPhase.Queued, Deadline = deadline };
        var sequence = checked(admission.Round.LastAdmissionSequence + 1); admission.Round.LastAdmissionSequence = sequence;
        admission.Match.ConcurrencyStamp = Guid.NewGuid();
        db.PatchUploads.Add(new() { Id = patchUploadId, CompetitionId = scope.CompetitionId,
            CompetitionChallengeId = admission.Question.CompetitionChallengeId, TeamId = admission.TeamId, UploadedByUserId = scope.UserId,
            RuntimeInstanceId = target.Id, FileId = fileId, UploadedAt = admittedAt });
        db.GameplayFacts.Add(fact); db.LiveSoloSubmissions.Add(new() { GameplayFactId = fact.Id, RoundId = admission.Round.Id,
            RoundQuestionId = admission.Question.Id, AdmissionSequence = sequence });
        target.GameplayFactId = fact.Id;
        if (target.ExpiresAt is { } originalExpiry && originalExpiry < deadline) target.ExpiresAt = deadline;
        replay?.Store(new AcceptedFixSubmission(patchUploadId, fact.Id, fact.State));
        await db.SaveChangesAsync(ct);
        await messages.PublishAsync(new StartFixExecution(fact.Id, target.Id));
        return new(PatchUploadSaveState.Accepted, fact.Id, fact.State);
    }, () => new(PatchUploadSaveState.ConcurrencyConflict), ct);
}
