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
        builder.Property(competition => competition.ManagerIds).HasColumnType("uuid[]");
        builder.Property(competition => competition.JudgeIds).HasColumnType("uuid[]");
        builder.Property(competition => competition.ObserverIds).HasColumnType("uuid[]");
        builder.Property(competition => competition.Mode).HasConversion<short>();
        builder.Property(competition => competition.Status).HasConversion<short>();
        builder.HasMany(competition => competition.LifecycleAudits)
            .WithOne()
            .HasForeignKey(audit => audit.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(competition => competition.DeletedAt == null);
        builder.HasIndex(competition => new { competition.Status, competition.StartAt });
        builder.HasIndex(competition => competition.ManagerIds).HasMethod("gin");
        builder.HasIndex(competition => competition.JudgeIds).HasMethod("gin");
        builder.HasIndex(competition => competition.ObserverIds).HasMethod("gin");
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany()
            .HasForeignKey(competition => competition.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_competitions_schedule",
            "start_at < end_at"));
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_competitions_permission_roles_exclusive",
                "NOT (manager_ids && judge_ids) AND NOT (manager_ids && observer_ids) AND NOT (judge_ids && observer_ids)");
            table.HasCheckConstraint(
                "ck_competitions_owner_not_permission",
                "NOT (owner_id = ANY(manager_ids)) AND NOT (owner_id = ANY(judge_ids)) AND NOT (owner_id = ANY(observer_ids))");
            table.HasCheckConstraint(
                "ck_competitions_flag_secret_length",
                "octet_length(flag_derivation_secret) = 32");
        });
    }
}

internal sealed class CompetitionLifecycleAuditConfiguration
    : IEntityTypeConfiguration<CompetitionLifecycleAudit>
{
    public void Configure(EntityTypeBuilder<CompetitionLifecycleAudit> builder)
    {
        builder.ToTable("competition_lifecycle_audits");
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.From).HasConversion<short>();
        builder.Property(audit => audit.To).HasConversion<short>();
        builder.HasIndex(audit => new { audit.CompetitionId, audit.OccurredAt });
    }
}
