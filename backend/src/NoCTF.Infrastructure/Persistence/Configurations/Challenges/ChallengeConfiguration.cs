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
        builder.HasQueryFilter(challenge => !challenge.Deletion.IsDeleted);
        builder.Property(challenge => challenge.Title).HasMaxLength(160);
        builder.Property(challenge => challenge.Direction).HasMaxLength(96);
        builder.OwnsOne(challenge => challenge.Deletion);
        builder.OwnsMany(challenge => challenge.Attachments, attachments =>
        {
            attachments.ToTable("challenge_attachments");
            attachments.WithOwner().HasForeignKey(attachment => attachment.ChallengeId);
            attachments.HasKey(attachment => attachment.Id);
            attachments.Property(attachment => attachment.ObjectKey).HasMaxLength(1024);
            attachments.HasIndex(attachment => attachment.ObjectKey).IsUnique();
        });
    }
}
