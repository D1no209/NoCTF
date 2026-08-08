using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.DataExports;

namespace NoCTF.Infrastructure.Persistence.Configurations.DataExports;

internal sealed class DataExportConfiguration : IEntityTypeConfiguration<DataExport>
{
    public void Configure(EntityTypeBuilder<DataExport> builder)
    {
        builder.ToTable("data_exports");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Scope).HasConversion<short>();
        builder.Property(item => item.Status).HasConversion<short>();
        builder.Property(item => item.FailureCode).HasConversion<short>();
        builder.HasIndex(item => new
        {
            item.RequestedByUserId,
            item.Scope,
            item.CompetitionId,
            item.RequestedAt
        });
        builder.HasIndex(item => item.Status);
        builder.HasIndex(item => item.PurgeAt);
        builder.HasIndex(item => new
        {
            item.RequestedByUserId,
            item.Scope,
            item.ActiveSlot
        }).IsUnique();
        builder.HasOne(item => item.File).WithMany()
            .HasForeignKey(item => item.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}
