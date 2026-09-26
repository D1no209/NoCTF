using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Progression;

public sealed class ProgressionChallengeStarter(
    IDbContextFactory<NoCtfDbContext> contexts,
    IExperimentalFeatureReader? experimentalFeatures = null)
    : IProgressionChallengeStarter
{
    public async Task<StartProgressionChallengeResult> StartAsync(
        Guid competitionId, Guid competitionChallengeId, Guid userId,
        long? expectedRevision, DateTimeOffset now, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, ct);
                var decision = await new CompetitionChallengeReadAccess(db).ResolveAsync(
                    userId, competitionId, now, ct);
                if (decision is null || !ParticipantChallengeVisibilityPolicy.CanView(
                        decision.Visibility.CompetitionStatus))
                    return StartProgressionChallengeResult.NotFound;

                var graph = decision.Visibility.GameMode == GameMode.Ctf
                    ? await db.CompetitionProgressions.AsNoTracking()
                        .Where(item => item.CompetitionId == competitionId)
                        .Select(item => new { item.Enabled, item.Revision })
                        .SingleOrDefaultAsync(ct)
                    : null;
                if (decision.Visibility.GameMode == GameMode.Ctf
                    && expectedRevision is long revision
                    && revision != (graph?.Revision ?? 0))
                    return StartProgressionChallengeResult.GraphChanged;

                var challengeId = await db.CompetitionChallenges.AsNoTracking()
                    .Where(item => item.Id == competitionChallengeId
                        && item.CompetitionId == competitionId && item.IsPublished)
                    .Join(db.Challenges.AsNoTracking(), instance => instance.ChallengeId,
                        template => template.Id, (_, template) => (Guid?)template.Id)
                    .SingleOrDefaultAsync(ct);
                if (challengeId is not Guid templateId)
                    return StartProgressionChallengeResult.NotFound;

                if (decision.Visibility.GameMode != GameMode.Ctf)
                    return StartProgressionChallengeResult.Started;

                if (decision.Visibility.CompetitionStatus is not (
                        CompetitionStatus.Running or CompetitionStatus.Paused))
                {
                    var interaction = await db.Set<CtfChallengeDefinition>().AsNoTracking()
                        .Where(definition => definition.ChallengeId == templateId)
                        .Select(definition => definition.InteractionKind)
                        .SingleOrDefaultAsync(ct);
                    if (interaction == CtfInteractionKind.PatchVerification
                        && !(await (experimentalFeatures?.IsCtfPatchVerificationEnabledAsync(ct)
                            ?? Task.FromResult(false))))
                        return StartProgressionChallengeResult.NotFound;
                }

                var nodeId = graph is not null
                    ? await db.ProgressionNodes.OfType<ChallengeProgressionNode>()
                        .AsNoTracking()
                        .Where(node => node.CompetitionId == competitionId
                            && node.CompetitionChallengeId == competitionChallengeId)
                        .Select(node => (Guid?)node.Id)
                        .SingleOrDefaultAsync(ct)
                    : null;
                if (nodeId is not Guid mappedNodeId)
                    return StartProgressionChallengeResult.Started;

                if (!await new ProgressionChallengeAccess(db).IsActiveAsync(
                        competitionId, competitionChallengeId, decision.TeamId, ct))
                    return StartProgressionChallengeResult.NotFound;
                if (decision.TeamId is not Guid teamId)
                    return StartProgressionChallengeResult.Started;

                var visited = await db.TeamProgressionNodeVisits.AsNoTracking()
                    .AnyAsync(item => item.TeamId == teamId && item.NodeId == mappedNodeId, ct);
                if (!visited)
                {
                    db.TeamProgressionNodeVisits.Add(new TeamProgressionNodeVisit
                    {
                        TeamId = teamId,
                        NodeId = mappedNodeId,
                        CompetitionId = competitionId,
                        FirstOpenedAt = now
                    });
                    await db.SaveChangesAsync(ct);
                }
                await transaction.CommitAsync(ct);
                return StartProgressionChallengeResult.Started;
            }
            catch (Exception exception) when (exception is DbUpdateConcurrencyException
                || exception is DbUpdateException
                || TransactionFailureClassifier.IsRetryable(exception))
            {
                if (attempt == 2)
                    return StartProgressionChallengeResult.GraphChanged;
                await Task.Delay(Random.Shared.Next(10, 40), ct);
            }
        }
        return StartProgressionChallengeResult.GraphChanged;
    }
}
