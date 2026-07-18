using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Storage;

namespace NoCTF.Infrastructure.Persistence.Configurations.Storage;

public sealed class FixUploadSessionConfiguration : IEntityTypeConfiguration<FixUploadSession>
{
    public void Configure(EntityTypeBuilder<FixUploadSession> builder)
    {
        builder.ToTable("fix_upload_sessions");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.ObjectKey).HasMaxLength(512).IsRequired();
        builder.Property(item => item.FileName).HasMaxLength(255).IsRequired();
        builder.Property(item => item.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(item => item.ExpectedSha256).HasMaxLength(64).IsRequired();
        builder.HasIndex(item => new { item.ObjectKey, item.Consumed });
        builder.HasIndex(item => new { item.UserId, item.ExpiresAt });
    }
}
