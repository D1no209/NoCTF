using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Infrastructure.Persistence.Configurations.Gameplay;

internal sealed class CapabilityOutcomeConfiguration : IEntityTypeConfiguration<CapabilityOutcome>
{
    public void Configure(EntityTypeBuilder<CapabilityOutcome> builder)
    {
        builder.ToTable("capability_outcomes");
        builder.HasKey(value => new { value.GameplayFactId, value.Kind });
        builder.HasDiscriminator(value => value.Kind)
            .HasValue<BreakOutcome>(ChallengeCapabilityKind.Break)
            .HasValue<FixOutcome>(ChallengeCapabilityKind.Fix)
            .HasValue<CheckOutcome>(ChallengeCapabilityKind.Check)
            .HasValue<ControlOutcome>(ChallengeCapabilityKind.Control)
            .HasValue<QuestionnaireOutcome>(ChallengeCapabilityKind.Questionnaire);
        builder.HasOne<NoCTF.Domain.Runtime.RuntimeInstance>().WithMany().HasForeignKey(value => value.RuntimeInstanceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BreakOutcomeConfiguration : IEntityTypeConfiguration<BreakOutcome>
{
    public void Configure(EntityTypeBuilder<BreakOutcome> builder) => builder.OwnsMany(value => value.Flags, flags =>
    {
        flags.ToTable("break_flag_matches");
        flags.Property<ChallengeCapabilityKind>("CapabilityKind");
        flags.WithOwner().HasForeignKey("GameplayFactId", "CapabilityKind");
        flags.HasKey(value => new { value.GameplayFactId, value.FlagId });
        flags.HasOne<NoCTF.Domain.Challenges.ChallengeFlag>().WithMany().HasForeignKey(value => value.FlagId).OnDelete(DeleteBehavior.Restrict);
    });
}

internal sealed class CheckOutcomeConfiguration : IEntityTypeConfiguration<CheckOutcome>
{
    public void Configure(EntityTypeBuilder<CheckOutcome> builder)
    {
        builder.OwnsMany(value => value.Probes, probes =>
        {
            probes.ToTable("check_probes");
            probes.Property<ChallengeCapabilityKind>("CapabilityKind");
            probes.WithOwner().HasForeignKey("GameplayFactId", "CapabilityKind");
            probes.HasKey(value => new { value.GameplayFactId, value.Position });
            probes.Property(value => value.Position).ValueGeneratedNever();
            probes.HasOne<NoCTF.Domain.Challenges.ChallengeFlag>().WithMany().HasForeignKey(value => value.FlagId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.OwnsMany(value => value.FlagContexts, contexts =>
        {
            contexts.ToTable("check_flag_contexts");
            contexts.Property<ChallengeCapabilityKind>("CapabilityKind");
            contexts.WithOwner().HasForeignKey("GameplayFactId", "CapabilityKind");
            contexts.HasKey(value => new { value.GameplayFactId, value.FlagId });
            contexts.HasOne<NoCTF.Domain.Challenges.ChallengeFlag>().WithMany().HasForeignKey(value => value.FlagId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class QuestionnaireOutcomeConfiguration : IEntityTypeConfiguration<QuestionnaireOutcome>
{
    public void Configure(EntityTypeBuilder<QuestionnaireOutcome> builder)
    {
        builder.HasOne<QuestionnaireRevision>().WithMany().HasForeignKey(value => value.RevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.OwnsMany(value => value.Questions, questions =>
        {
            questions.ToTable("questionnaire_question_results");
            questions.Property<ChallengeCapabilityKind>("CapabilityKind");
            questions.WithOwner().HasForeignKey("GameplayFactId", "CapabilityKind");
            questions.HasKey(value => new { value.GameplayFactId, value.QuestionId });
        });
    }
}
