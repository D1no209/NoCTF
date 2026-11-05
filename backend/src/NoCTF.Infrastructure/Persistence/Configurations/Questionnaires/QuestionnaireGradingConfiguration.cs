using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Infrastructure.Persistence.Configurations.Questionnaires;

internal sealed class QuestionnaireGradingConfiguration : IEntityTypeConfiguration<QuestionnaireGradingOverride>, IEntityTypeConfiguration<QuestionnaireGradingOption>
{
    public void Configure(EntityTypeBuilder<QuestionnaireGradingOverride> builder)
    {
        builder.ToTable("questionnaire_grading_overrides");
        builder.HasKey(item => new { item.ActivityId, item.RevisionId });
        builder.HasOne<QuestionnaireActivity>().WithMany().HasForeignKey(item => item.ActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QuestionnaireQuestion>().WithMany().HasForeignKey(item => item.RevisionId)
            .HasPrincipalKey(question => question.VersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(item => item.Options).WithOne().HasForeignKey(item => new { item.ActivityId, item.RevisionId }).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<QuestionnaireGradingOption> builder)
    {
        builder.ToTable("questionnaire_grading_options");
        builder.HasKey(item => new { item.ActivityId, item.RevisionId, item.OptionId });
        builder.HasOne<QuestionnaireOption>().WithMany().HasForeignKey(item => new { item.RevisionId, item.OptionId }).OnDelete(DeleteBehavior.Restrict);
    }
}
