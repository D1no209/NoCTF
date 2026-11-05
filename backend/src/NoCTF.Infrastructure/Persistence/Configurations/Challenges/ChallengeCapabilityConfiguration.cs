using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Storage;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class ChallengeCapabilityConfiguration : IEntityTypeConfiguration<ChallengeCapabilityDefinition>
{
    public void Configure(EntityTypeBuilder<ChallengeCapabilityDefinition> builder)
    {
        builder.ToTable("challenge_capabilities");
        builder.HasKey(value => new { value.ChallengeId, value.Kind });
        builder.HasDiscriminator(value => value.Kind)
            .HasValue<BreakCapabilityDefinition>(ChallengeCapabilityKind.Break)
            .HasValue<FixCapabilityDefinition>(ChallengeCapabilityKind.Fix)
            .HasValue<CheckCapabilityDefinition>(ChallengeCapabilityKind.Check)
            .HasValue<ControlCapabilityDefinition>(ChallengeCapabilityKind.Control)
            .HasValue<QuestionnaireCapabilityDefinition>(ChallengeCapabilityKind.Questionnaire);
        builder.Property(value => value.Kind).HasConversion<short>();
    }
}

internal sealed class BreakCapabilityConfiguration : IEntityTypeConfiguration<BreakCapabilityDefinition>
{
    public void Configure(EntityTypeBuilder<BreakCapabilityDefinition> builder)
    {
        builder.ComplexProperty(value => value.FlagTemplate);
        builder.OwnsMany(value => value.EnvironmentTargets, targets =>
        {
            targets.ToTable("challenge_flag_environment_targets");
            targets.Property<ChallengeCapabilityKind>("CapabilityKind");
            targets.WithOwner().HasForeignKey("ChallengeId", "CapabilityKind");
            targets.HasKey(value => new { value.ChallengeId, value.ServiceName });
            targets.HasIndex(value => new { value.ChallengeId, value.Position }).IsUnique();
        });
        builder.OwnsMany(value => value.FlagStores, stores =>
        {
            stores.ToTable("challenge_flag_stores");
            stores.Property<ChallengeCapabilityKind>("CapabilityKind");
            stores.WithOwner().HasForeignKey("ChallengeId", "CapabilityKind");
            stores.HasKey(value => new { value.ChallengeId, value.Id });
            stores.HasIndex(value => new { value.ChallengeId, value.Position }).IsUnique();
            stores.HasIndex(value => new { value.ChallengeId, value.Name }).IsUnique();
        });
    }
}

internal sealed class FixCapabilityConfiguration : IEntityTypeConfiguration<FixCapabilityDefinition>
{
    public void Configure(EntityTypeBuilder<FixCapabilityDefinition> builder) => builder.OwnsMany(value => value.Command, command =>
    {
        command.ToTable("challenge_fix_command");
        command.Property<ChallengeCapabilityKind>("CapabilityKind");
        command.WithOwner().HasForeignKey("ChallengeId", "CapabilityKind");
        command.HasKey(value => new { value.ChallengeId, value.Position });
        command.Property(value => value.Position).ValueGeneratedNever();
    });
}

internal sealed class CheckCapabilityConfiguration : IEntityTypeConfiguration<CheckCapabilityDefinition>
{
    public void Configure(EntityTypeBuilder<CheckCapabilityDefinition> builder)
    {
        builder.OwnsMany(value => value.Command, command =>
        {
            command.ToTable("challenge_check_command");
            command.Property<ChallengeCapabilityKind>("CapabilityKind");
            command.WithOwner().HasForeignKey("ChallengeId", "CapabilityKind");
            command.HasKey(value => new { value.ChallengeId, value.Position });
            command.Property(value => value.Position).ValueGeneratedNever();
        });
        builder.OwnsMany(value => value.Environment, environment =>
        {
            environment.ToTable("challenge_check_environment");
            environment.Property<ChallengeCapabilityKind>("CapabilityKind");
            environment.WithOwner().HasForeignKey("ChallengeId", "CapabilityKind");
            environment.HasKey(value => new { value.ChallengeId, value.Name });
        });
        builder.OwnsMany(value => value.Requirements, requirements =>
        {
            requirements.ToTable("challenge_check_requirements");
            requirements.Property<ChallengeCapabilityKind>("CapabilityKind");
            requirements.WithOwner().HasForeignKey("ChallengeId", "CapabilityKind");
            requirements.HasKey(value => new { value.ChallengeId, value.Kind });
        });
    }
}

internal sealed class QuestionnaireCapabilityConfiguration : IEntityTypeConfiguration<QuestionnaireCapabilityDefinition>
{
    public void Configure(EntityTypeBuilder<QuestionnaireCapabilityDefinition> builder)
    {
        builder.HasOne(value => value.CurrentRevision).WithMany()
            .HasForeignKey(value => new { value.CurrentRevisionId, value.ChallengeId })
            .HasPrincipalKey(value => new { value.Id, value.ChallengeId }).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(value => value.CurrentRevision).AutoInclude();
    }
}

internal sealed class QuestionnaireRevisionConfiguration : IEntityTypeConfiguration<QuestionnaireRevision>
{
    public void Configure(EntityTypeBuilder<QuestionnaireRevision> builder)
    {
        builder.ToTable("questionnaire_revisions");
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.Id, value.ChallengeId });
        builder.HasIndex(value => new { value.ChallengeId, value.Number }).IsUnique();
        builder.HasOne<NoCTF.Domain.Challenges.Challenge>().WithMany().HasForeignKey(value => value.ChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(value => value.Questions).WithOne(value => value.Revision).HasForeignKey(value => value.RevisionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(value => value.Questions).AutoInclude();
    }
}

internal sealed class QuestionnaireQuestionConfiguration : IEntityTypeConfiguration<QuestionnaireQuestion>
{
    public void Configure(EntityTypeBuilder<QuestionnaireQuestion> builder)
    {
        builder.ToTable("questionnaire_questions");
        builder.HasKey(value => new { value.RevisionId, value.Id });
        builder.HasAlternateKey(value => value.VersionId);
        builder.HasAlternateKey(value => new { value.VersionId, value.Id });
        builder.HasAlternateKey(value => new { value.VersionId, value.RevisionId, value.Id });
        builder.HasIndex(value => new { value.RevisionId, value.Position }).IsUnique();
        builder.HasOne<QuestionnaireQuestionSource>().WithMany()
            .HasForeignKey(value => value.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(value => value.Options).WithOne()
            .HasForeignKey(value => new { value.QuestionVersionId, value.RevisionId, value.QuestionId })
            .HasPrincipalKey(value => new { value.VersionId, value.RevisionId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(value => value.Options).AutoInclude();
        builder.OwnsMany(value => value.Classifications, classifications =>
        {
            classifications.ToTable("questionnaire_classifications");
            classifications.WithOwner().HasForeignKey(value => new { value.RevisionId, value.QuestionId });
            classifications.HasKey(value => new { value.RevisionId, value.QuestionId, value.Kind, value.NormalizedName });
        });
        builder.OwnsMany(value => value.Files, files =>
        {
            files.ToTable("questionnaire_files");
            files.WithOwner().HasForeignKey(value => new { value.RevisionId, value.QuestionId });
            files.HasKey(value => new { value.RevisionId, value.QuestionId, value.FileId });
            files.HasOne<StoredFile>().WithMany().HasForeignKey(value => value.FileId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class QuestionnaireOptionConfiguration : IEntityTypeConfiguration<QuestionnaireOption>
{
    public void Configure(EntityTypeBuilder<QuestionnaireOption> builder)
    {
        builder.ToTable("questionnaire_options");
        builder.HasKey(value => new { value.QuestionVersionId, value.Id });
        builder.HasIndex(value => new { value.QuestionVersionId, value.Position }).IsUnique();
    }
}
