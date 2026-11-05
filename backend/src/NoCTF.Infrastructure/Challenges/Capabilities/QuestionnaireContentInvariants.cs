using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Infrastructure.Challenges.Capabilities;

internal static class QuestionnaireContentInvariants
{
    public static void Validate(ChangeTracker tracker)
    {
        if (Changed<QuestionnaireRevision>(tracker) || Changed<QuestionnaireQuestion>(tracker)
            || Changed<QuestionnaireOption>(tracker) || Changed<QuestionnaireClassification>(tracker)
            || Changed<QuestionnaireFile>(tracker))
            throw new InvalidOperationException("Published questionnaire content is immutable; create a new revision.");
        var revisions = tracker.Entries<QuestionnaireRevision>().Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.Id).ToHashSet();
        if (tracker.Entries<QuestionnaireQuestion>().Any(entry => entry.State == EntityState.Added && !revisions.Contains(entry.Entity.RevisionId))
            || tracker.Entries<QuestionnaireOption>().Any(entry => entry.State == EntityState.Added && !revisions.Contains(entry.Entity.RevisionId))
            || tracker.Entries<QuestionnaireClassification>().Any(entry => entry.State == EntityState.Added && !revisions.Contains(entry.Entity.RevisionId))
            || tracker.Entries<QuestionnaireFile>().Any(entry => entry.State == EntityState.Added && !revisions.Contains(entry.Entity.RevisionId)))
            throw new InvalidOperationException("Questionnaire content cannot be appended to an existing revision.");
    }

    private static bool Changed<T>(ChangeTracker tracker) where T : class =>
        tracker.Entries<T>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
}
