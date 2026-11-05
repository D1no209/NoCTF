using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Questionnaires.Sessions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class QuestionnaireAssignmentConfiguration : IEntityTypeConfiguration<QuestionnaireAssignment>
{
    public void Configure(EntityTypeBuilder<QuestionnaireAssignment> builder)
    {
        builder.ToTable("questionnaire_assignments");
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.Id, value.SessionId, value.RevisionId });
        builder.HasIndex(value => new { value.SessionId, value.RevisionId }).IsUnique();
        builder.HasOne<QuestionnaireSession>().WithMany(value => value.Assignments).HasForeignKey(value => value.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QuestionnaireRevision>().WithMany().HasForeignKey(value => new { value.RevisionId, value.ChallengeId })
            .HasPrincipalKey(value => new { value.Id, value.ChallengeId }).OnDelete(DeleteBehavior.Restrict);
    }
}
