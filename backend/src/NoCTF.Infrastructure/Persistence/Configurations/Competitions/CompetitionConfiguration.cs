using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

internal sealed class CompetitionEntityConfiguration : IEntityTypeConfiguration<Competition>
{
    public void Configure(EntityTypeBuilder<Competition> builder)
    {
        builder.ToTable("competitions");
        builder.HasKey(competition => competition.Id);
        builder.Property(competition => competition.Title).HasMaxLength(160);
        builder.Property(competition => competition.ConfigurationJson).HasColumnType("jsonb");
        builder.OwnsOne(competition => competition.Deletion);
        builder.OwnsMany(competition => competition.Collaborators, collaborators =>
        {
            collaborators.ToTable("competition_collaborators");
            collaborators.WithOwner().HasForeignKey("CompetitionId");
            collaborators.HasKey(collaborator => collaborator.Id);
            collaborators.HasIndex("CompetitionId", nameof(CompetitionCollaborator.UserId)).IsUnique();
        });
        builder.OwnsMany(competition => competition.LifecycleAudits, audits =>
        {
            audits.ToTable("competition_lifecycle_audits");
            audits.WithOwner().HasForeignKey("CompetitionId");
            audits.HasKey(audit => audit.Id);
            audits.Property(audit => audit.Reason).HasMaxLength(256);
            audits.HasIndex("CompetitionId", nameof(CompetitionLifecycleAudit.OccurredAt));
        });
        builder.HasQueryFilter(competition => !competition.Deletion.IsDeleted);
        builder.HasIndex(competition => new { competition.Status, competition.StartTime });
    }
}
