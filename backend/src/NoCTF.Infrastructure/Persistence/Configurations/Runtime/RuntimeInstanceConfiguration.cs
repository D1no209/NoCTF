using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Persistence.Configurations.Runtime;

internal sealed class RuntimeInstanceConfiguration : IEntityTypeConfiguration<RuntimeInstance>
{
    public void Configure(EntityTypeBuilder<RuntimeInstance> builder)
    {
        builder.ToTable("runtime_instances", table =>
        {
            table.HasCheckConstraint(
                "ck_runtime_instances_failure",
                "(state = 5) = (failure_code IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_runtime_instances_checker_sequence",
                "last_applied_checker_sequence <= checker_sequence");
            table.HasCheckConstraint(
                "ck_runtime_instances_awd_checker_target",
                "awd_checker_target_host IS NULL OR runtime_kind IN (0, 1)");
            table.HasCheckConstraint(
                "ck_runtime_instances_awdp_gameplay_fact",
                "(purpose = 1) = (gameplay_fact_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_runtime_instances_awdp_fix_stage",
                "awdp_fix_stage IS NULL OR purpose = 1");
        });
        builder.HasKey(instance => instance.Id);
        builder.HasAlternateKey(instance => new { instance.Id, instance.CompetitionId });
        builder.Property(instance => instance.RuntimeKind).HasConversion<short>();
        builder.Property(instance => instance.Purpose).HasConversion<short>();
        builder.Property(instance => instance.AwdpFixStage).HasConversion<short>();
        builder.Property(instance => instance.RuntimeProvider).HasConversion<short>();
        builder.Property(instance => instance.State).HasConversion<short>();
        builder.Property(instance => instance.FailureCode).HasConversion<short>();
        builder.Property(instance => instance.CheckerStatus).HasConversion<short>();
        builder.Property(instance => instance.ProcessingVersion).IsConcurrencyToken();
        builder.Property(instance => instance.ProviderReceiptJson).HasColumnType("jsonb");
        builder.Property(instance => instance.Urls).HasColumnType("text[]");
        builder.Property(instance => instance.ParticipantUrlIndexes).HasColumnType("integer[]");
        builder.HasIndex(instance => new
        {
            instance.CompetitionChallengeId,
            instance.TeamId,
            instance.Generation
        }).IsUnique();
        builder.HasIndex(instance => new
        {
            instance.CompetitionChallengeId,
            instance.TeamId
        }).IsUnique().HasFilter("purpose IN (0, 2, 3) AND state IN (0, 1, 2)");
        builder.HasIndex(instance => instance.GameplayFactId)
            .IsUnique()
            .HasFilter("purpose = 1 AND gameplay_fact_id IS NOT NULL AND state IN (0, 1, 2, 3)");
        builder.HasIndex(instance => new
        {
            instance.RunnerPool,
            instance.State,
            instance.CreatedAt,
            instance.Id
        });
        builder.HasIndex(instance => new { instance.State, instance.NextCheckerDueAt })
            .HasFilter("next_checker_due_at IS NOT NULL");
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany()
            .HasForeignKey(instance => instance.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Challenges.CompetitionChallenge>().WithMany()
            .HasForeignKey(instance => instance.CompetitionChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Teams.Team>().WithMany()
            .HasForeignKey(instance => instance.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Gameplay.GameplayFact>().WithMany()
            .HasForeignKey(instance => instance.GameplayFactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RuntimeInstance>().WithMany()
            .HasForeignKey(instance => instance.ReplacesRuntimeInstanceId).OnDelete(DeleteBehavior.Restrict);
        builder.OwnsMany(instance => instance.PublishedPorts, ports =>
        {
            ports.ToJson("published_ports_json");
            ports.Property(port => port.ServiceName).HasMaxLength(63);
        });
    }
}
