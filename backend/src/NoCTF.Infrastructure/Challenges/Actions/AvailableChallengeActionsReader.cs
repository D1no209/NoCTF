using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Actions;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Actions;

public sealed class AvailableChallengeActionsReader(NoCtfDbContext db, IChallengeRuntimeTemplateCatalog templates,
    IGameplayFactAdmissionModePolicy modes) : IAvailableChallengeActionsReader
{
    public async Task<IReadOnlyList<AvailableChallengeAction>> ReadAsync(Guid competitionId, Guid challengeId, Guid actorId,
        DateTimeOffset now, CancellationToken ct)
    {
        var scope = await GameplayFactAdmissionPersistence.LoadAsync(db, competitionId, challengeId, actorId, now, templates, modes, ct);
        if (scope is null || scope.Mode == GameMode.LiveSolo) return [];
        var actions = new List<AvailableChallengeAction>();
        var rules = modes.GetRules(scope.Mode, scope.CompetitionConfiguration, scope.ChallengeRules, scope.ChallengeDefinition);
        if (rules.AllowsFlag)
        {
            var admission = GameplayFactAdmissionPolicy.Check(scope, GameplayFactKind.BreakAttempt, rules, now);
            var practice = GameplayFactAdmissionPolicy.IsPracticeFlagAttempt(scope, GameplayFactKind.BreakAttempt, now);
            var remaining = !practice && rules.MaxFlagAttempts is > 0 ? Math.Max(0, rules.MaxFlagAttempts.Value - scope.AcceptedFlagAttempts) : (int?)null;
            actions.Add(new(ChallengeActionKind.SubmitFlag, admission.Succeeded,
                admission.Succeeded ? ChallengeActionAvailability.Available : ChallengeActionAvailability.AdmissionRejected,
                admission.FailureCode, RemainingAttempts: remaining));
        }
        if (scope.Mode is GameMode.Awd or GameMode.Awdp && scope.ChallengeDefinition?.Check is not null)
            actions.Add(new(ChallengeActionKind.ViewCheck, true, ChallengeActionAvailability.Available));
        if (scope.Mode == GameMode.Koh && scope.ChallengeDefinition?.Control is not null)
            actions.Add(new(ChallengeActionKind.ViewControl, true, ChallengeActionAvailability.Available));
        if (!rules.AllowsFix) return actions;
        var fixAdmission = GameplayFactAdmissionPolicy.Check(scope, GameplayFactKind.FixAttempt, rules, now);
        var purpose = scope.Mode == GameMode.Awd ? RuntimePurpose.Player : RuntimePurpose.FixTarget;
        var target = await db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes().Where(runtime => runtime.CompetitionId == competitionId
                && runtime.CompetitionChallengeId == challengeId && runtime.TeamId == scope.TeamId && runtime.Purpose == purpose
                && (runtime.State == RuntimeState.Queued || runtime.State == RuntimeState.Provisioning || runtime.State == RuntimeState.Running || runtime.State == RuntimeState.Stopping))
            .OrderByDescending(runtime => runtime.CreatedAt).ThenByDescending(runtime => runtime.Id)
            .Select(runtime => new { runtime.Id, runtime.State, runtime.GameplayFactId }).FirstOrDefaultAsync(ct);
        var remainingFixes = rules.MaxFixAttempts is > 0 ? Math.Max(0, rules.MaxFixAttempts.Value - scope.AcceptedFixAttempts) : (int?)null;
        if (scope.Mode == GameMode.Awdp)
            actions.Add(new(ChallengeActionKind.PrepareFixTarget, fixAdmission.Succeeded && target is null,
                !fixAdmission.Succeeded ? ChallengeActionAvailability.AdmissionRejected : target is null ? ChallengeActionAvailability.Available : ChallengeActionAvailability.TargetBusy,
                fixAdmission.FailureCode, target?.Id, remainingFixes));
        var availability = !fixAdmission.Succeeded ? ChallengeActionAvailability.AdmissionRejected
            : target is null ? scope.Mode == GameMode.Awd ? ChallengeActionAvailability.TargetNotRunning : ChallengeActionAvailability.TargetRequired
            : target.State == RuntimeState.Stopping || scope.Mode == GameMode.Awd && target.State != RuntimeState.Running
                ? ChallengeActionAvailability.TargetNotRunning
            : scope.Mode == GameMode.Awdp && target.GameplayFactId is not null ? ChallengeActionAvailability.TargetBusy
            : ChallengeActionAvailability.Available;
        Guid? currentFactId = scope.Mode == GameMode.Awdp ? target?.GameplayFactId : null;
        if (target is not null && scope.Mode == GameMode.Awd)
        {
            var execution = await db.GameplayFacts.AsNoTracking().Where(fact => fact.FixExecution != null && fact.FixExecution.RuntimeInstanceId == target.Id
                    && (fact.State == GameplayFactState.Pending || fact.State == GameplayFactState.Processing || fact.FixExecution.MutationBlocked))
                .Select(fact => new { fact.Id, fact.State, fact.FixExecution!.MutationBlocked }).SingleOrDefaultAsync(ct);
            currentFactId = execution?.Id;
            if (execution is not null && fixAdmission.Succeeded)
                availability = execution.State is not (GameplayFactState.Pending or GameplayFactState.Processing) && execution.MutationBlocked
                    ? ChallengeActionAvailability.RecoveryRequired : ChallengeActionAvailability.TargetBusy;
        }
        actions.Add(new(ChallengeActionKind.SubmitFix, availability == ChallengeActionAvailability.Available, availability,
            fixAdmission.FailureCode, target?.Id, remainingFixes, currentFactId));
        return actions;
    }
}
