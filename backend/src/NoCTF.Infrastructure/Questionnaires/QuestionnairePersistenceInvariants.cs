using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Infrastructure.Questionnaires;

internal static class QuestionnairePersistenceInvariants
{
    public static void Validate(ChangeTracker tracker)
    {
        if (Mutated<QuestionnaireDrawReceipt>(tracker) || Mutated<QuestionnaireIssueReceipt>(tracker)
            || Mutated<QuestionnaireAnswerReceipt>(tracker) || Mutated<QuestionnaireActivityOperation>(tracker)
            || Mutated<QuestionnaireQuestionExposure>(tracker))
            throw new InvalidOperationException("Questionnaire receipts, exposure and audit records are immutable.");
        foreach (var entry in tracker.Entries<QuestionnaireQuestionSource>())
        {
            if (entry.State == EntityState.Deleted) throw new InvalidOperationException("Canonical questionnaire sources are retained.");
            if (entry.State == EntityState.Modified && entry.OriginalValues.GetValue<DateTimeOffset?>(nameof(QuestionnaireQuestionSource.FirstExposedAt)) is { } original
                && (entry.Entity.FirstExposedAt is null || entry.Entity.FirstExposedAt > original))
                throw new InvalidOperationException("Questionnaire exposure history cannot be cleared or moved forward.");
        }
        foreach (var entry in tracker.Entries<QuestionnaireParticipant>().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.State == EntityState.Modified && entry.Properties.Any(property => property.IsModified
                && property.Metadata.Name is nameof(QuestionnaireParticipant.ActivityId) or nameof(QuestionnaireParticipant.UserId)))
                throw new InvalidOperationException("A questionnaire participant cannot change its activity or identity.");
            var participant = entry.Entity;
            if (participant.UserId == Guid.Empty) throw new InvalidOperationException("A questionnaire participant must reference a real user.");
        }
    }

    private static bool Mutated<T>(ChangeTracker tracker) where T : class => tracker.Entries<T>()
        .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
}
