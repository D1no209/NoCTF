using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Theory;

internal sealed class TheoryGradingConfiguration : IEntityTypeConfiguration<TheoryGradingOverride>, IEntityTypeConfiguration<TheoryGradingOption>
{
    public void Configure(EntityTypeBuilder<TheoryGradingOverride> builder)
    {
        builder.ToTable("theory_grading_overrides");
        builder.HasKey(item => new { item.ActivityId, item.RevisionId });
        builder.HasOne<TheoryActivity>().WithMany().HasForeignKey(item => item.ActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TheoryQuestionRevision>().WithMany().HasForeignKey(item => item.RevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(item => item.Options).WithOne().HasForeignKey(item => new { item.ActivityId, item.RevisionId }).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<TheoryGradingOption> builder)
    {
        builder.ToTable("theory_grading_options");
        builder.HasKey(item => new { item.ActivityId, item.RevisionId, item.OptionId });
        builder.HasOne<TheoryQuestionOption>().WithMany().HasForeignKey(item => item.OptionId).OnDelete(DeleteBehavior.Restrict);
    }
}
