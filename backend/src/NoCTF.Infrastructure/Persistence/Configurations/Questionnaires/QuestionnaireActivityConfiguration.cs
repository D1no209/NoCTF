using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Infrastructure.Persistence.Configurations.Questionnaires;

internal sealed class QuestionnaireActivityConfiguration : IEntityTypeConfiguration<QuestionnaireActivity>,
    IEntityTypeConfiguration<QuestionnaireDrawRule>, IEntityTypeConfiguration<QuestionnaireDrawRuleFilter>,
    IEntityTypeConfiguration<QuestionnaireDrawCandidate>, IEntityTypeConfiguration<QuestionnaireActivityPause>,
    IEntityTypeConfiguration<QuestionnaireActivityOperation>, IEntityTypeConfiguration<QuestionnaireQuestionExposure>,
    IEntityTypeConfiguration<QuestionnaireFixedSelection>
{
    public void Configure(EntityTypeBuilder<QuestionnaireActivity> builder)
    {
        builder.ToTable("questionnaire_activities");
        builder.HasOne<User>().WithMany().HasForeignKey(activity => activity.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(activity => activity.Rules).WithOne().HasForeignKey(rule => rule.ActivityId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(activity => activity.FixedQuestionnaires).WithOne().HasForeignKey(value => value.ActivityId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(activity => activity.FixedQuestionnaires).AutoInclude();
        builder.HasMany(activity => activity.Pauses).WithOne().HasForeignKey(pause => pause.ActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(activity => new { activity.State, activity.ClosesAt });
    }

    public void Configure(EntityTypeBuilder<QuestionnaireFixedSelection> builder)
    {
        builder.ToTable("questionnaire_activity_fixed_questionnaires");
        builder.HasKey(value => new { value.ActivityId, value.Position });
        builder.HasIndex(value => new { value.ActivityId, value.ChallengeId }).IsUnique();
        builder.HasOne<NoCTF.Domain.Challenges.Challenge>().WithMany().HasForeignKey(value => value.ChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QuestionnaireRevision>().WithMany().HasForeignKey(value => new { value.RevisionId, value.ChallengeId })
            .HasPrincipalKey(value => new { value.Id, value.ChallengeId }).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<QuestionnaireDrawRule> builder)
    {
        builder.ToTable("questionnaire_draw_rules");
        builder.HasIndex(rule => new { rule.ActivityId, rule.Order }).IsUnique();
        builder.HasMany(rule => rule.Filters).WithOne().HasForeignKey(filter => filter.RuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(rule => rule.Candidates).WithOne().HasForeignKey(candidate => candidate.RuleId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<QuestionnaireDrawRuleFilter> builder)
    {
        builder.ToTable("questionnaire_draw_rule_filters");
        builder.HasKey(filter => new { filter.RuleId, filter.Kind, filter.NormalizedName });
    }

    public void Configure(EntityTypeBuilder<QuestionnaireDrawCandidate> builder)
    {
        builder.ToTable("questionnaire_draw_candidates");
        builder.HasKey(candidate => new { candidate.RuleId, candidate.RevisionId });
        builder.HasOne<QuestionnaireQuestion>().WithMany().HasForeignKey(candidate => candidate.RevisionId)
            .HasPrincipalKey(question => question.VersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QuestionnaireQuestionSource>().WithMany().HasForeignKey(candidate => candidate.SourceId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<QuestionnaireActivityPause> builder)
    {
        builder.ToTable("questionnaire_activity_pauses");
        builder.HasIndex(pause => new { pause.ActivityId, pause.StartedAt });
    }

    public void Configure(EntityTypeBuilder<QuestionnaireActivityOperation> builder)
    {
        builder.ToTable("questionnaire_activity_operations");
        builder.HasOne<QuestionnaireActivity>().WithMany().HasForeignKey(operation => operation.ActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(operation => operation.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QuestionnaireSession>().WithMany().HasForeignKey(operation => operation.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QuestionnaireQuestion>().WithMany().HasForeignKey(operation => operation.RevisionId)
            .HasPrincipalKey(question => question.VersionId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<QuestionnaireQuestionExposure> builder)
    {
        builder.ToTable("questionnaire_question_exposures");
        builder.HasKey(exposure => new { exposure.SourceId, exposure.ActivityId });
        builder.HasOne<QuestionnaireQuestionSource>().WithMany().HasForeignKey(exposure => exposure.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QuestionnaireActivity>().WithMany().HasForeignKey(exposure => exposure.ActivityId).OnDelete(DeleteBehavior.Restrict);
    }
}
