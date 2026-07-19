using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

internal sealed class CompetitionLifecycleAuditConfiguration : IEntityTypeConfiguration<CompetitionLifecycleAudit>
{
    public void Configure(EntityTypeBuilder<CompetitionLifecycleAudit> builder)
    {
        builder.ToTable("competition_lifecycle_audits");
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.Reason).HasMaxLength(256);
        builder.HasIndex(audit => new { audit.CompetitionId, audit.OccurredAt });
        builder.HasOne<Competition>().WithMany().HasForeignKey(audit => audit.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
