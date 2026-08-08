using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Storage;

namespace NoCTF.Infrastructure.Persistence.Configurations.Storage;

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("files", table =>
        {
            table.HasCheckConstraint("ck_files_byte_length", "byte_length >= 0");
            table.HasCheckConstraint("ck_files_sha256_length", "octet_length(sha256) = 32");
        });
        builder.HasKey(file => file.Id);
        builder.HasIndex(file => file.ObjectKey).IsUnique();
    }
}
