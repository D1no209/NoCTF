using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Bank;

public sealed class ChallengeCapabilityUsageGuard(NoCtfDbContext db)
{
    public async Task<ChallengeTemplateWriteState?> CheckAsync(Guid challengeId, ChallengeDefinition previous, ChallengeDefinition next, CancellationToken ct)
    {
        var removed = previous.Capabilities.Select(value => value.Kind).Except(next.Capabilities.Select(value => value.Kind)).ToArray();
        var changed = ChallengeDefinitionStructuralComparer.ChangedExecutionCapabilities(previous, next);
        var runtimeChanged = ChallengeDefinitionStructuralComparer.HasRuntimeChanges(previous, next);
        if (removed.Length == 0 && changed.Count == 0 && !runtimeChanged) return null;
        var entries = await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking().Where(value => value.ChallengeId == challengeId)
            .Select(value => value.Id).ToArrayAsync(ct);
        var rules = await db.Set<CompetitionChallengeRules>().AsNoTracking().Where(value => entries.Contains(value.CompetitionChallengeId))
            .AsSplitQuery().ToDictionaryAsync(value => value.CompetitionChallengeId, ct);
        if (rules.Values.Any(value => GameModeChallengeConfigurationCatalog.RequiredCapabilities(value).Intersect(removed).Any()))
            return ChallengeTemplateWriteState.ReferencedCapabilityConflict;
        var facts = db.GameplayFacts.AsNoTracking().Where(value => value.CompetitionChallengeId != null && entries.Contains(value.CompetitionChallengeId.Value));
        if (removed.Length > 0)
        {
            var kinds = Enum.GetValues<GameplayFactKind>().Where(kind => FactCapabilities(kind, null).Intersect(removed).Any()).ToArray();
            if (await facts.AnyAsync(value => kinds.Contains(value.Kind) || value.CapabilityOutcomes.Any(outcome => removed.Contains(outcome.Kind)), ct))
                return ChallengeTemplateWriteState.ReferencedCapabilityConflict;
            if (removed.Contains(ChallengeCapabilityKind.Questionnaire)
                && (await db.Set<QuestionnaireAssignment>().AnyAsync(value => value.ChallengeId == challengeId, ct)
                    || await db.Set<NoCTF.Domain.Questionnaires.Activities.QuestionnaireFixedSelection>().AnyAsync(value => value.ChallengeId == challengeId, ct)
                    || await db.Set<NoCTF.Domain.Questionnaires.Activities.QuestionnaireDrawCandidate>().AnyAsync(candidate => db.Set<QuestionnaireQuestion>()
                        .Any(question => question.VersionId == candidate.RevisionId && question.Revision.ChallengeId == challengeId), ct)))
                return ChallengeTemplateWriteState.ReferencedCapabilityConflict;
        }
        var pending = await facts.Where(value => value.State == GameplayFactState.Pending || value.State == GameplayFactState.Queued || value.State == GameplayFactState.Processing)
            .Select(value => new { value.Kind, TargetKind = value.FixExecution == null ? (FixTargetKind?)null : value.FixExecution.TargetKind }).ToArrayAsync(ct);
        if (pending.Any(value => FactCapabilities(value.Kind, value.TargetKind) is { Count: > 0 } capabilities
                && (runtimeChanged || capabilities.Intersect(changed).Any()))) return ChallengeTemplateWriteState.PendingExecutionConflict;
        var runtimes = await db.RuntimeInstances.AsNoTracking().Where(value =>
                (value.State == RuntimeState.Queued || value.State == RuntimeState.Provisioning || value.State == RuntimeState.Running || value.State == RuntimeState.Stopping)
                && (value.ChallengeId == challengeId || value.CompetitionChallengeId != null && entries.Contains(value.CompetitionChallengeId.Value)))
            .Select(value => new { value.Purpose, value.TestFlagDelivery, value.CompetitionChallengeId,
                TestKind = value.CapabilityTest == null ? (NoCTF.Domain.Challenges.Testing.TemplateCapabilityTestKind?)null : value.CapabilityTest.Kind }).ToArrayAsync(ct);
        foreach (var runtime in runtimes)
        {
            IReadOnlyList<ChallengeCapabilityKind> capabilities = runtime.Purpose switch
            {
                RuntimePurpose.TemplateTest => runtime.TestKind is not null
                    ? [ChallengeCapabilityKind.Break, ChallengeCapabilityKind.Fix, ChallengeCapabilityKind.Check, ChallengeCapabilityKind.Control]
                    : runtime.TestFlagDelivery is RuntimeTestFlagDelivery.Environment or RuntimeTestFlagDelivery.Command ? [ChallengeCapabilityKind.Break] : [],
                RuntimePurpose.AttackTarget => [ChallengeCapabilityKind.Break],
                RuntimePurpose.FixTarget or RuntimePurpose.ScopedFixTarget => [ChallengeCapabilityKind.Fix, ChallengeCapabilityKind.Check],
                _ => GameModeChallengeConfigurationCatalog.RequiredCapabilities(rules[runtime.CompetitionChallengeId!.Value])
            };
            if (runtimeChanged || capabilities.Intersect(changed).Any()) return ChallengeTemplateWriteState.ActiveRuntimeDefinitionConflict;
        }
        return null;
    }

    private static IReadOnlyList<ChallengeCapabilityKind> FactCapabilities(GameplayFactKind kind, FixTargetKind? target) => kind switch
    {
        GameplayFactKind.BreakAttempt => [ChallengeCapabilityKind.Break],
        GameplayFactKind.FixAttempt => target == FixTargetKind.Persistent ? [ChallengeCapabilityKind.Fix] : [ChallengeCapabilityKind.Fix, ChallengeCapabilityKind.Check],
        GameplayFactKind.CheckObservation => [ChallengeCapabilityKind.Check],
        GameplayFactKind.ControlObservation => [ChallengeCapabilityKind.Control],
        GameplayFactKind.QuestionnaireAttempt => [ChallengeCapabilityKind.Questionnaire],
        GameplayFactKind.HintUnlock or GameplayFactKind.ManualAdjustment or GameplayFactKind.AttachmentDownload or GameplayFactKind.WriteUpUnlock => [],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
