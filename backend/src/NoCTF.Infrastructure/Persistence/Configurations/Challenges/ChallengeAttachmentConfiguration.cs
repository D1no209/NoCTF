using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class ChallengeAttachmentConfiguration : IEntityTypeConfiguration<ChallengeAttachment>
{
    public void Configure(EntityTypeBuilder<ChallengeAttachment> builder)
    {
        builder.ToTable("challenge_attachments");
        builder.HasKey(attachment => attachment.Id);
        builder.Property(attachment => attachment.ObjectKey).HasMaxLength(1024);
        builder.HasIndex(attachment => attachment.ObjectKey).IsUnique();
    }
}
