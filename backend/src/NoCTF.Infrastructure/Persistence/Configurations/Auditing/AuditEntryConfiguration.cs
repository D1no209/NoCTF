using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Auditing;

namespace NoCTF.Infrastructure.Persistence.Configurations.Auditing;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.MetadataJson).HasColumnType("jsonb");
        builder.HasIndex(entry => new { entry.CompetitionId, entry.OccurredAt });
    }
}
