using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Questionnaires.Sessions;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Questionnaires;

public sealed class QuestionnaireScheduleSource(NoCtfDbContext db) : IClusterScheduleContributor
{
    public async Task<IReadOnlyList<ClusterScheduleEntry>> RebuildAsync(DateTimeOffset now, CancellationToken ct)
    {
        var activities = await db.QuestionnaireActivities.AsNoTracking().Where(activity => activity.State != QuestionnaireActivityState.Draft
            && db.QuestionnaireParticipants.Any(participant => participant.ActivityId == activity.Id
                && db.QuestionnaireSessions.Any(session => session.ParticipantId == participant.Id && session.State == QuestionnaireSessionState.Active)))
            .Select(activity => activity.Id).ToArrayAsync(ct);
        var corrections = await db.Set<QuestionnaireGradingOverride>().AsNoTracking().Where(rule => rule.AppliedRevision != rule.TargetRevision)
            .Select(rule => new NoCTF.Application.Questionnaires.Activities.ApplyQuestionnaireCorrection(rule.ActivityId, rule.RevisionId, rule.TargetRevision)).ToArrayAsync(ct);
        return activities.Select(id => new ClusterScheduleEntry($"questionnaire:{id:N}", ClusterScheduleKind.Questionnaire,
            now, TimeSpan.FromSeconds(5), new SweepQuestionnaireActivity(id)))
            .Concat(corrections.Select(message => new ClusterScheduleEntry($"questionnaire-correction:{message.ActivityId:N}:{message.RevisionId:N}",
                ClusterScheduleKind.Questionnaire, now, TimeSpan.FromSeconds(5), message))).ToArray();
    }
}
