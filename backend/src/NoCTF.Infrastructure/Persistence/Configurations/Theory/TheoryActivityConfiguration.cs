using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Theory;

internal sealed class TheoryActivityConfiguration : IEntityTypeConfiguration<TheoryActivity>,
    IEntityTypeConfiguration<CompetitionTheoryActivity>, IEntityTypeConfiguration<TheoryDrawRule>,
    IEntityTypeConfiguration<TheoryDrawRuleFilter>, IEntityTypeConfiguration<TheoryDrawCandidate>,
    IEntityTypeConfiguration<TheoryActivityPause>, IEntityTypeConfiguration<TheoryActivityOperation>,
    IEntityTypeConfiguration<TheoryQuestionExposure>
{
    public void Configure(EntityTypeBuilder<TheoryActivity> builder)
    {
        builder.ToTable("theory_activities");
        builder.HasOne<User>().WithMany().HasForeignKey(activity => activity.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(activity => activity.Rules).WithOne().HasForeignKey(rule => rule.ActivityId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(activity => activity.Pauses).WithOne().HasForeignKey(pause => pause.ActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(activity => new { activity.State, activity.ClosesAt });
    }

    public void Configure(EntityTypeBuilder<CompetitionTheoryActivity> builder)
    {
        builder.ToTable("competition_theory_activities");
        builder.HasKey(link => link.CompetitionId);
        builder.HasIndex(link => link.ActivityId).IsUnique();
        builder.HasOne<Competition>().WithMany().HasForeignKey(link => link.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TheoryActivity>().WithOne().HasForeignKey<CompetitionTheoryActivity>(link => link.ActivityId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TheoryDrawRule> builder)
    {
        builder.ToTable("theory_draw_rules");
        builder.HasIndex(rule => new { rule.ActivityId, rule.Order }).IsUnique();
        builder.HasMany(rule => rule.Filters).WithOne().HasForeignKey(filter => filter.RuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(rule => rule.Candidates).WithOne().HasForeignKey(candidate => candidate.RuleId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<TheoryDrawRuleFilter> builder)
    {
        builder.ToTable("theory_draw_rule_filters");
        builder.HasKey(filter => new { filter.RuleId, filter.Kind, filter.NormalizedName });
    }

    public void Configure(EntityTypeBuilder<TheoryDrawCandidate> builder)
    {
        builder.ToTable("theory_draw_candidates");
        builder.HasKey(candidate => new { candidate.RuleId, candidate.RevisionId });
        builder.HasOne<TheoryQuestionRevision>().WithMany().HasForeignKey(candidate => candidate.RevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TheoryQuestionSource>().WithMany().HasForeignKey(candidate => candidate.SourceId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TheoryActivityPause> builder)
    {
        builder.ToTable("theory_activity_pauses");
        builder.HasIndex(pause => new { pause.ActivityId, pause.StartedAt });
    }

    public void Configure(EntityTypeBuilder<TheoryActivityOperation> builder)
    {
        builder.ToTable("theory_activity_operations");
        builder.HasOne<TheoryActivity>().WithMany().HasForeignKey(operation => operation.ActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(operation => operation.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TheoryPaper>().WithMany().HasForeignKey(operation => operation.PaperId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TheoryQuestionRevision>().WithMany().HasForeignKey(operation => operation.RevisionId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TheoryQuestionExposure> builder)
    {
        builder.ToTable("theory_question_exposures");
        builder.HasKey(exposure => new { exposure.SourceId, exposure.ActivityId });
        builder.HasOne<TheoryQuestionSource>().WithMany().HasForeignKey(exposure => exposure.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TheoryActivity>().WithMany().HasForeignKey(exposure => exposure.ActivityId).OnDelete(DeleteBehavior.Restrict);
    }
}
