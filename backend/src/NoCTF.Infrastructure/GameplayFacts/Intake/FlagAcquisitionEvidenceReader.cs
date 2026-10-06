using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.Intake;

internal static class FlagAcquisitionEvidenceReader
{
    public static async Task<FlagAcquisitionEvidence> CaptureAsync(NoCtfDbContext db,
        GameplayFactAdmissionSnapshot scope, IChallengeRuntimeTemplateCatalog templates,
        DateTimeOffset submittedAt, bool practice, CancellationToken ct)
    {
        var evidence = new FlagAcquisitionEvidence { CapturedAt = submittedAt };
        var runtime = templates.Get(scope.ChallengeDefinition);
        if (scope.Mode != GameMode.Ctf || practice
            || scope.ChallengeDefinition is not CtfChallengeDefinition { InteractionKind: CtfInteractionKind.FlagSubmission }
            || runtime is { FlagSource: not RuntimeFlagSource.Static })
            return evidence;
        evidence.Scope = FlagAcquisitionScope.FormalStaticCtf;
        if (runtime is { RuntimeKind: RuntimeKind.Container })
            evidence.Required |= FlagAcquisitionResource.Container;
        var templateId = await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.Id == scope.CompetitionChallengeId).Select(item => item.ChallengeId).SingleAsync(ct);
        if (await db.Set<ChallengeAttachment>().AsNoTracking()
            .AnyAsync(item => item.ChallengeId == templateId && item.DeletedAt == null, ct))
            evidence.Required |= FlagAcquisitionResource.Attachment;

        var legacy = await db.GameplayFacts.AsNoTracking().AnyAsync(fact =>
            fact.CompetitionId == scope.CompetitionId && fact.CompetitionChallengeId == scope.CompetitionChallengeId
            && fact.TeamId == scope.TeamId && fact.Kind == GameplayFactKind.FlagAttempt
            && fact.AcquisitionEvidence == null && fact.OccurredAt <= submittedAt, ct);
        if (legacy)
        {
            evidence.Source = FlagAcquisitionEvidenceSource.LegacySubmission;
            evidence.Acquired = FlagAcquisitionResource.Container | FlagAcquisitionResource.Attachment;
            return evidence;
        }
        var started = await db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes()
            .Where(instance => instance.CompetitionId == scope.CompetitionId
                && instance.CompetitionChallengeId == scope.CompetitionChallengeId && instance.TeamId == scope.TeamId
                && instance.Purpose == RuntimePurpose.Player && instance.RuntimeKind == RuntimeKind.Container
                && instance.RunningAt != null && instance.RunningAt <= submittedAt)
            .OrderBy(instance => instance.RunningAt).ThenBy(instance => instance.Id)
            .Select(instance => new { instance.Id, instance.RunningAt }).FirstOrDefaultAsync(ct);
        if (started is not null)
        {
            evidence.Acquired |= FlagAcquisitionResource.Container;
            evidence.RuntimeInstanceId = started.Id;
            evidence.RuntimeStartedAt = started.RunningAt;
        }
        var downloaded = await db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == scope.CompetitionId
                && fact.CompetitionChallengeId == scope.CompetitionChallengeId && fact.TeamId == scope.TeamId
                && fact.Kind == GameplayFactKind.AttachmentDownload && fact.State == GameplayFactState.Completed
                && fact.Result == GameplayFactResult.Applied && fact.OccurredAt <= submittedAt)
            .OrderBy(fact => fact.OccurredAt).ThenBy(fact => fact.Id)
            .Select(fact => new { fact.Id, fact.OccurredAt }).FirstOrDefaultAsync(ct);
        if (downloaded is not null)
        {
            evidence.Acquired |= FlagAcquisitionResource.Attachment;
            evidence.AttachmentDownloadFactId = downloaded.Id;
            evidence.AttachmentDownloadedAt = downloaded.OccurredAt;
        }
        return evidence;
    }
}
