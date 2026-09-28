using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Domain.Commands;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.GameplayFacts.PatchVerification;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.PatchVerification.Configuration;
using NoCTF.GameModes.PatchVerification.Runtime;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.Infrastructure.GameplayFacts.PatchVerification;

public sealed class PatchVerificationTargetStore(
    NoCtfDbContext db,
    GameplayFactAttemptCriticalSection criticalSection,
    IRuntimePlacementPolicy placementPolicy,
    TeamRuntimeQuota runtimeQuota,
    IPostCommitMessagePublisher outbox,
    ICompetitionEventRecorder? eventRecorder = null,
    IRequestReplay? replay = null,
    IExperimentalFeatureReader? experimentalFeatures = null) : IPatchVerificationTargetStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<PatchVerificationTargetRequestResult> TryCreateAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var teamId = await ResolveTeamIdAsync(competitionId, userId, ct);
        if (teamId is null)
            return new(PatchVerificationTargetRequestState.ScopeNotFound);

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        using var quotaLease = await runtimeQuota.AcquireLockAsync(
            db,
            competitionId,
            teamId.Value,
            ct);
        using var lease = await criticalSection.AcquireAsync(
            db,
            teamId.Value,
            competitionChallengeId,
            GameplayFactKind.FixAttempt,
            ct);
        var prior = replay is null
            ? null
            : await replay.FindAsync<PatchVerificationTargetRequestResult>(
                new(userId, ReplayOperation.PatchVerificationTarget, competitionId, competitionChallengeId),
                new EmptyReplayFingerprint(),
                ct);
        if (prior is not null)
            return prior;

        var context = await LoadContextAsync(
            competitionId,
            competitionChallengeId,
            includeActiveCompetitionOnly: true,
            ct);
        if (context is null)
            return new(PatchVerificationTargetRequestState.ScopeNotFound);
        if (context.Definition is not CtfChallengeDefinition
            { InteractionKind: CtfInteractionKind.PatchVerification })
            return new(PatchVerificationTargetRequestState.ScopeNotFound);

        var admission = await GameplayFactAdmissionPersistence.LoadAsync(
            db,
            competitionId,
            competitionChallengeId,
            userId,
            ct);
        if (admission is null)
            return new(PatchVerificationTargetRequestState.ScopeNotFound);
        if (admission.HasCorrectFix)
            return new(PatchVerificationTargetRequestState.AchievementAlreadySucceeded);

        var configuration = PatchVerificationConfigurationResolver.Resolve(
            GameMode.Ctf,
            context.CompetitionConfiguration,
            context.Rules,
            context.Definition);
        if (configuration is null)
            return new(PatchVerificationTargetRequestState.InvalidConfiguration);
        if (configuration.MaximumAttempts > 0
            && admission.AcceptedFixAttempts >= configuration.MaximumAttempts)
        {
            return new(PatchVerificationTargetRequestState.AttemptsExhausted);
        }
        if (!await runtimeQuota.CanCreateSlotAsync(
            db,
            competitionId,
            teamId.Value,
            competitionChallengeId,
            context.MaxConcurrentRuntimeInstancesPerTeam,
            ct))
        {
            return new(PatchVerificationTargetRequestState.RuntimeQuotaExceeded);
        }
        var activeTargetExists = await db.RuntimeInstances.AnyAsync(instance =>
            instance.CompetitionId == competitionId
            && instance.CompetitionChallengeId == competitionChallengeId
            && instance.TeamId == teamId
            && instance.Purpose == RuntimePurpose.PatchVerificationTarget
            && (instance.State == RuntimeState.Queued
                || instance.State == RuntimeState.Provisioning
                || instance.State == RuntimeState.Running
                || instance.State == RuntimeState.Stopping
                || instance.State == RuntimeState.Failed
                    && instance.ProviderReceipt != null),
            ct);
        if (activeTargetExists)
            return new(PatchVerificationTargetRequestState.ActiveTargetExists);

        RuntimePlacement placement;
        try
        {
            placement = placementPolicy.Resolve(configuration.Runtime.RuntimeKind);
            _ = PatchVerificationTargetDefinitionFactory.Create(
                Guid.Empty,
                configuration.Runtime,
                placement.Provider,
                RuntimePurpose.PatchVerificationTarget);
        }
        catch (InvalidOperationException)
        {
            return new(PatchVerificationTargetRequestState.InvalidConfiguration);
        }

        var runtimeId = Guid.CreateVersion7(now);
        var target = PatchVerificationTargetRuntimeFactory.Create(
            teamId.Value,
            null,
            competitionId,
            competitionChallengeId,
            runtimeId,
            configuration.Runtime,
            placement,
            RuntimePurpose.PatchVerificationTarget,
            now);
        db.RuntimeInstances.Add(target);
        await outbox.PublishAsync(new DispatchRuntime(target.Id));
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.RuntimeCreated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            now,
            ActorUserId: userId,
            TeamId: teamId,
            CompetitionChallengeId: competitionChallengeId,
            RuntimeInstanceId: runtimeId,
            RuntimeState: RuntimeState.Queued), ct);
        try
        {
            var result = new PatchVerificationTargetRequestResult(
                PatchVerificationTargetRequestState.Created,
                runtimeId,
                RuntimeState.Queued);
            replay?.Store(result);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await outbox.FlushCommittedMessagesAsync();
            return result;
        }
        catch (DbUpdateException exception) when (!TransactionFailureClassifier.IsRetryable(exception))
        {
            return new(PatchVerificationTargetRequestState.ConcurrencyConflict);
        }
    }

    public async Task<PatchVerificationParticipantState?> GetStateAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct)
    {
        var teamId = await ResolveTeamIdAsync(competitionId, userId, ct);
        if (teamId is null)
            return null;
        var context = await LoadContextAsync(
            competitionId,
            competitionChallengeId,
            includeActiveCompetitionOnly: false,
            ct);
        if (context is null
            || context.Definition is not CtfChallengeDefinition
                { InteractionKind: CtfInteractionKind.PatchVerification })
        {
            return null;
        }
        if (context.Status is not (CompetitionStatus.Running or CompetitionStatus.Paused)
            && !(await IsPatchVerificationEnabledAsync(ct)))
        {
            return null;
        }
        var configuration = PatchVerificationConfigurationResolver.Resolve(
            GameMode.Ctf,
            context.CompetitionConfiguration,
            context.Rules,
            context.Definition);
        if (configuration is null)
            return null;

        var facts = await db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == competitionId
                && fact.CompetitionChallengeId == competitionChallengeId
                && fact.TeamId == teamId
                && fact.Kind == GameplayFactKind.FixAttempt)
            .OrderByDescending(fact => fact.OccurredAt)
            .ThenByDescending(fact => fact.Id)
            .Select(fact => new { fact.State, fact.Result, fact.FailureCode })
            .ToArrayAsync(ct);
        var target = await db.RuntimeInstances.AsNoTracking()
            .Where(instance => instance.CompetitionId == competitionId
                && instance.CompetitionChallengeId == competitionChallengeId
                && instance.TeamId == teamId
                && instance.Purpose == RuntimePurpose.PatchVerificationTarget
                && instance.State != RuntimeState.Stopped)
            .OrderByDescending(instance => instance.CreatedAt)
            .Select(instance => new { instance.Id, instance.State })
            .FirstOrDefaultAsync(ct);
        var accepted = facts.Count(fact => fact.State != GameplayFactState.PlatformFailed);
        return new(
            context.Status is CompetitionStatus.Running or CompetitionStatus.Paused
                && context.IsPublished,
            configuration.MaximumAttempts,
            accepted,
            Math.Max(0, configuration.MaximumAttempts - accepted),
            facts.FirstOrDefault()?.State,
            facts.FirstOrDefault()?.Result,
            facts.FirstOrDefault()?.FailureCode,
            target?.Id,
            target?.State);
    }

    private Task<Guid?> ResolveTeamIdAsync(Guid competitionId, Guid userId, CancellationToken ct) =>
        db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && team.DeletedAt == null
                && !team.IsBanned
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && team.Members.Any(member => member.UserId == userId))
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(ct);

    private Task<bool> IsPatchVerificationEnabledAsync(CancellationToken ct) =>
        experimentalFeatures?.IsCtfPatchVerificationEnabledAsync(ct)
        ?? Task.FromResult(false);

    private Task<PatchVerificationContext?> LoadContextAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        bool includeActiveCompetitionOnly,
        CancellationToken ct)
    {
        var challenges = db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == competitionChallengeId
                && challenge.CompetitionId == competitionId
                && challenge.DeletedAt == null);
        var competitions = db.Competitions.AsNoTracking().Where(competition =>
            competition.Id == competitionId
            && competition.DeletedAt == null
            && competition.Mode == GameMode.Ctf);
        if (includeActiveCompetitionOnly)
        {
            challenges = challenges.Where(challenge => challenge.IsPublished);
            competitions = competitions.Where(competition =>
                competition.Status == CompetitionStatus.Running
                || competition.Status == CompetitionStatus.Paused);
        }
        return challenges
            .Join(
                competitions,
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { challenge, competition })
            .Join(
                db.Challenges.AsNoTracking().Where(template => template.DeletedAt == null),
                item => item.challenge.ChallengeId,
                template => template.Id,
                (item, template) => new PatchVerificationContext(
                    item.competition.ModeConfiguration!,
                    item.challenge.Rules!,
                    template.Definition!,
                    item.competition.Status,
                    item.challenge.IsPublished,
                    item.competition.MaxConcurrentRuntimeInstancesPerTeam))
            .AsSplitQuery()
            .SingleOrDefaultAsync(ct);
    }

    private sealed record PatchVerificationContext(
        CompetitionModeConfiguration CompetitionConfiguration,
        CompetitionChallengeRules Rules,
        ChallengeDefinition Definition,
        CompetitionStatus Status,
        bool IsPublished,
        int MaxConcurrentRuntimeInstancesPerTeam);
}
