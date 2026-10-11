using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Theory.Activities;

internal static class TheoryCompetitionDeletion
{
    // Called only inside the authorized force-deletion transaction, after competition gameplay facts.
    internal static async Task DeleteAsync(NoCtfDbContext db, Guid competitionId, CancellationToken ct)
    {
        var activities = await db.CompetitionTheoryActivities.Where(link => link.CompetitionId == competitionId).Select(link => link.ActivityId).ToArrayAsync(ct);
        if (activities.Length == 0) return;
        var participants = await db.TheoryParticipants.Where(item => activities.Contains(item.ActivityId)).Select(item => item.Id).ToArrayAsync(ct);
        var papers = await db.TheoryPapers.Where(item => participants.Contains(item.ParticipantId)).Select(item => item.Id).ToArrayAsync(ct);
        var items = await db.Set<TheoryPaperItem>().Where(item => papers.Contains(item.PaperId)).Select(item => item.Id).ToArrayAsync(ct);
        var rules = await db.Set<TheoryDrawRule>().Where(item => activities.Contains(item.ActivityId)).Select(item => item.Id).ToArrayAsync(ct);
        var exposure = await db.Set<TheoryQuestionExposure>().Where(item => activities.Contains(item.ActivityId))
            .GroupBy(item => item.SourceId).Select(group => new { Id = group.Key, At = group.Min(item => item.FirstExposedAt) }).ToArrayAsync(ct);
        foreach (var source in exposure)
        {
            var stamp = Guid.NewGuid();
            await db.Set<NoCTF.Domain.Theory.Questions.TheoryQuestionSource>().Where(item => item.Id == source.Id
                    && (item.FirstExposedAt == null || item.FirstExposedAt > source.At))
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.FirstExposedAt, source.At)
                    .SetProperty(item => item.ConcurrencyStamp, stamp), ct);
        }
        await db.Set<TheoryActivityOperation>().Where(item => activities.Contains(item.ActivityId)).ExecuteDeleteAsync(ct);
        await db.Set<TheoryAnswerReceipt>().Where(item => items.Contains(item.ItemId)).ExecuteDeleteAsync(ct);
        await db.Set<TheoryPaperOption>().Where(item => items.Contains(item.ItemId)).ExecuteDeleteAsync(ct);
        await db.Set<TheoryPaperItem>().Where(item => items.Contains(item.Id)).ExecuteDeleteAsync(ct);
        await db.Set<TheoryDrawReceipt>().Where(item => participants.Contains(item.ParticipantId)).ExecuteDeleteAsync(ct);
        await db.Set<TheoryIssueReceipt>().Where(item => participants.Contains(item.ParticipantId)).ExecuteDeleteAsync(ct);
        await db.Set<TheoryActivePaper>().Where(item => participants.Contains(item.ParticipantId)).ExecuteDeleteAsync(ct);
        await db.TheoryPapers.Where(item => papers.Contains(item.Id)).ExecuteDeleteAsync(ct);
        await db.TheoryParticipants.Where(item => participants.Contains(item.Id)).ExecuteDeleteAsync(ct);
        await db.Set<TheoryGradingOption>().Where(item => activities.Contains(item.ActivityId)).ExecuteDeleteAsync(ct);
        await db.Set<TheoryGradingOverride>().Where(item => activities.Contains(item.ActivityId)).ExecuteDeleteAsync(ct);
        await db.Set<TheoryDrawCandidate>().Where(item => rules.Contains(item.RuleId)).ExecuteDeleteAsync(ct);
        await db.Set<TheoryDrawRuleFilter>().Where(item => rules.Contains(item.RuleId)).ExecuteDeleteAsync(ct);
        await db.Set<TheoryDrawRule>().Where(item => rules.Contains(item.Id)).ExecuteDeleteAsync(ct);
        await db.Set<TheoryActivityPause>().Where(item => activities.Contains(item.ActivityId)).ExecuteDeleteAsync(ct);
        await db.Set<TheoryQuestionExposure>().Where(item => activities.Contains(item.ActivityId)).ExecuteDeleteAsync(ct);
        await db.CompetitionTheoryActivities.Where(item => item.CompetitionId == competitionId).ExecuteDeleteAsync(ct);
        await db.TheoryActivities.Where(item => activities.Contains(item.Id)).ExecuteDeleteAsync(ct);
    }
}
