using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Infrastructure.Theory;

internal static class TheoryPersistenceInvariants
{
    public static void Validate(ChangeTracker tracker)
    {
        if (Mutated<TheoryQuestionRevision>(tracker) || Mutated<TheoryQuestionOption>(tracker)
            || Mutated<TheoryQuestionClassification>(tracker) || Mutated<TheoryQuestionFile>(tracker)
            || Mutated<TheoryDrawReceipt>(tracker) || Mutated<TheoryIssueReceipt>(tracker)
            || Mutated<TheoryAnswerReceipt>(tracker) || Mutated<TheoryActivityOperation>(tracker)
            || Mutated<TheoryQuestionExposure>(tracker))
            throw new InvalidOperationException("Theory content versions, draw receipts and audit records are immutable.");
        foreach (var entry in tracker.Entries<TheoryQuestionSource>())
        {
            if (entry.State == EntityState.Deleted) throw new InvalidOperationException("Canonical theory sources are retained.");
            if (entry.State == EntityState.Modified && entry.OriginalValues.GetValue<DateTimeOffset?>(nameof(TheoryQuestionSource.FirstExposedAt)) is { } original
                && (entry.Entity.FirstExposedAt is null || entry.Entity.FirstExposedAt > original))
                throw new InvalidOperationException("Theory exposure history cannot be cleared or moved forward.");
        }
        var newRevisions = tracker.Entries<TheoryQuestionRevision>().Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.Id).ToHashSet();
        if (tracker.Entries<TheoryQuestionOption>().Any(entry => entry.State == EntityState.Added && !newRevisions.Contains(entry.Entity.RevisionId))
            || tracker.Entries<TheoryQuestionClassification>().Any(entry => entry.State == EntityState.Added && !newRevisions.Contains(entry.Entity.RevisionId))
            || tracker.Entries<TheoryQuestionFile>().Any(entry => entry.State == EntityState.Added && !newRevisions.Contains(entry.Entity.RevisionId)))
            throw new InvalidOperationException("Children cannot be appended to an existing theory content version.");
        if (tracker.Entries<TheoryQuestion>().Any(entry => entry.State == EntityState.Modified
            && entry.Property(question => question.SourceId).IsModified))
            throw new InvalidOperationException("Canonical theory source identity is immutable.");
        foreach (var entry in tracker.Entries<TheoryParticipant>().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.State == EntityState.Modified && entry.Properties.Any(property => property.IsModified
                && property.Metadata.Name is nameof(TheoryParticipant.ActivityId) or nameof(TheoryParticipant.Kind)
                    or nameof(TheoryParticipant.TeamId) or nameof(TheoryParticipant.UserId)))
                throw new InvalidOperationException("A theory participant cannot change its activity or identity.");
            var participant = entry.Entity;
            if (participant.Kind switch
                {
                    TheoryParticipantKind.Individual => participant.UserId is null || participant.TeamId is not null,
                    TheoryParticipantKind.Team => participant.TeamId is null || participant.UserId is not null,
                    _ => true
                }) throw new InvalidOperationException("A theory participant must reference exactly its declared identity kind.");
        }
    }

    private static bool Mutated<T>(ChangeTracker tracker) where T : class => tracker.Entries<T>()
        .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);
}
