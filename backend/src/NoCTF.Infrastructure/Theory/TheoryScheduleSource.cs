using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Theory.Papers;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Theory;

public sealed class TheoryScheduleSource(NoCtfDbContext db) : IClusterScheduleContributor
{
    public async Task<IReadOnlyList<ClusterScheduleEntry>> RebuildAsync(DateTimeOffset now, CancellationToken ct)
    {
        var activities = await db.TheoryActivities.AsNoTracking().Where(activity => activity.State != TheoryActivityState.Draft
            && db.TheoryParticipants.Any(participant => participant.ActivityId == activity.Id
                && db.TheoryPapers.Any(paper => paper.ParticipantId == participant.Id && paper.State == TheoryPaperState.Active)))
            .Select(activity => activity.Id).ToArrayAsync(ct);
        var corrections = await db.Set<TheoryGradingOverride>().AsNoTracking().Where(rule => rule.AppliedRevision != rule.TargetRevision)
            .Select(rule => new NoCTF.Application.Theory.Activities.ApplyTheoryCorrection(rule.ActivityId, rule.RevisionId, rule.TargetRevision)).ToArrayAsync(ct);
        return activities.Select(id => new ClusterScheduleEntry($"theory:{id:N}", ClusterScheduleKind.Theory,
            now, TimeSpan.FromSeconds(5), new SweepTheoryActivity(id)))
            .Concat(corrections.Select(message => new ClusterScheduleEntry($"theory-correction:{message.ActivityId:N}:{message.RevisionId:N}",
                ClusterScheduleKind.Theory, now, TimeSpan.FromSeconds(5), message))).ToArray();
    }
}
