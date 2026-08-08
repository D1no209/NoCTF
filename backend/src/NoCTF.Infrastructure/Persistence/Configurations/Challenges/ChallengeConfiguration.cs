using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class ChallengeEntityConfiguration : IEntityTypeConfiguration<Challenge>
{
    public void Configure(EntityTypeBuilder<Challenge> builder)
    {
        builder.ToTable("challenges");
        builder.HasKey(challenge => challenge.Id);
        builder.HasQueryFilter(challenge => challenge.DeletedAt == null);
        builder.Property(challenge => challenge.Title).HasMaxLength(160);
        builder.Property(challenge => challenge.Direction).HasMaxLength(96);
        builder.Property(challenge => challenge.ManagerIds).HasColumnType("uuid[]");
        builder.Property(challenge => challenge.Mode).HasConversion<short>();
        // PostgreSQL jsonb is required for versioned, mode-specific executable definitions.
        builder.Property(challenge => challenge.DefinitionJson).HasColumnType("jsonb");
        builder.Property(challenge => challenge.Visibility).HasConversion<short>();
        builder.HasIndex(challenge => challenge.ManagerIds).HasMethod("gin");
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany()
            .HasForeignKey(challenge => challenge.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_challenges_owner_not_manager",
            "NOT (owner_id = ANY(manager_ids))"));
        builder.HasMany(challenge => challenge.Attachments)
            .WithOne()
            .HasForeignKey(attachment => attachment.ChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ChallengeAttachmentConfiguration
    : IEntityTypeConfiguration<ChallengeAttachment>
{
    public void Configure(EntityTypeBuilder<ChallengeAttachment> builder)
    {
        builder.ToTable("challenge_attachments");
        builder.HasKey(attachment => attachment.Id);
        builder.HasOne(attachment => attachment.File).WithMany()
            .HasForeignKey(attachment => attachment.FileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
