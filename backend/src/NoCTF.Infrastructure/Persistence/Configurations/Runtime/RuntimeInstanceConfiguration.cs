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
                "ck_runtime_instances_awdp_gameplay_fact",
                "gameplay_fact_id IS NULL OR purpose = 1");
        });
        builder.HasKey(instance => instance.Id);
        builder.HasAlternateKey(instance => new { instance.Id, instance.CompetitionId });
        builder.Property(instance => instance.RuntimeKind).HasConversion<short>();
        builder.Property(instance => instance.Purpose).HasConversion<short>();
        builder.Property(instance => instance.RuntimeProvider).HasConversion<short>();
        builder.Property(instance => instance.State).HasConversion<short>();
        builder.Property(instance => instance.FailureCode).HasConversion<short>();
        builder.Property(instance => instance.ProviderReceiptJson).HasColumnType("jsonb");
        builder.Property(instance => instance.Urls).HasColumnType("text[]");
        builder.HasIndex(instance => new
        {
            instance.CompetitionChallengeId,
            instance.TeamId,
            instance.Purpose,
            instance.CreatedAt,
            instance.Id
        });
        builder.HasIndex(instance => new
        {
            instance.CompetitionChallengeId,
            instance.TeamId
        }).IsUnique().HasFilter("purpose IN (0, 2, 3) AND state IN (0, 1, 2)");
        builder.HasIndex(instance => instance.GameplayFactId)
            .IsUnique()
            .HasFilter("purpose = 1 AND gameplay_fact_id IS NOT NULL AND state IN (0, 1, 2, 3)");
        builder.HasIndex(instance => new { instance.RunnerId, instance.State, instance.CreatedAt, instance.Id });
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany()
            .HasForeignKey(instance => instance.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Challenges.CompetitionChallenge>().WithMany()
            .HasForeignKey(instance => instance.CompetitionChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Teams.Team>().WithMany()
            .HasForeignKey(instance => instance.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Gameplay.GameplayFact>().WithMany()
            .HasForeignKey(instance => instance.GameplayFactId).OnDelete(DeleteBehavior.Restrict);
        builder.OwnsMany(instance => instance.PublishedPorts, ports =>
        {
            ports.ToJson("published_ports_json");
            ports.Property(port => port.ServiceName).HasMaxLength(63);
        });
    }
}
