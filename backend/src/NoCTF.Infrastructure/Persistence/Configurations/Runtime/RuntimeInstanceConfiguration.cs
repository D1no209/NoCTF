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
            table.HasCheckConstraint(
                "ck_runtime_instances_scope",
                "(purpose = 4 AND challenge_id IS NOT NULL AND competition_id IS NULL AND competition_challenge_id IS NULL AND team_id IS NULL AND gameplay_fact_id IS NULL) OR " +
                "(purpose <> 4 AND challenge_id IS NULL AND competition_id IS NOT NULL AND competition_challenge_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_runtime_instances_test_flag",
                "(purpose = 4) = (test_flag_delivery IS NOT NULL AND test_flag_state IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_runtime_instances_traffic_capture",
                "(traffic_capture_limit_bytes IS NULL OR traffic_capture_limit_bytes > 0) AND traffic_capture_reserved_bytes >= 0");
        });
        builder.HasKey(instance => instance.Id);
        builder.Property(instance => instance.RuntimeKind).HasConversion<short>();
        builder.Property(instance => instance.Purpose).HasConversion<short>();
        builder.Property(instance => instance.AccessMode).HasConversion<short>();
        builder.Property(instance => instance.TestFlagDelivery).HasConversion<short>();
        builder.Property(instance => instance.TestFlagState).HasConversion<short>();
        builder.Property(instance => instance.RuntimeProvider).HasConversion<short>();
        builder.Property(instance => instance.State).HasConversion<short>();
        builder.Property(instance => instance.FailureCode).HasConversion<short>();
        builder.Property(instance => instance.ProviderReceiptJson).HasColumnType("jsonb");
        // PostgreSQL jsonb stores the bounded, typed active allocation document for Redis recovery.
        builder.Property(instance => instance.CapacityAllocations)
            .HasColumnType("jsonb")
            .HasDefaultValue(RuntimeCapacityAllocations.Empty)
            .HasConversion(
                value => RuntimeCapacityAllocations.Serialize(value),
                json => RuntimeCapacityAllocations.Deserialize(json))
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<RuntimeCapacityAllocations>(
                (left, right) => RuntimeCapacityAllocations.Serialize(left!) == RuntimeCapacityAllocations.Serialize(right!),
                value => RuntimeCapacityAllocations.Serialize(value).GetHashCode(),
                value => RuntimeCapacityAllocations.Deserialize(RuntimeCapacityAllocations.Serialize(value))));
        // PostgreSQL jsonb keeps the ordered, provider-resolved access endpoints on the
        // owning Runtime row without introducing a forbidden runtime-artifacts table.
        builder.OwnsMany(instance => instance.AccessEndpoints, endpoints =>
        {
            endpoints.ToJson("access_endpoints_json");
            endpoints.Property(endpoint => endpoint.TargetHost).HasMaxLength(255);
        });
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
        builder.HasIndex(instance => instance.ChallengeId)
            .IsUnique()
            .HasFilter("purpose = 4 AND state IN (0, 1, 2)");
        builder.HasIndex(instance => instance.GameplayFactId)
            .IsUnique()
            .HasFilter("purpose = 1 AND gameplay_fact_id IS NOT NULL AND state IN (0, 1, 2, 3)");
        builder.HasIndex(instance => new { instance.RunnerId, instance.State, instance.CreatedAt, instance.Id });
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany()
            .HasForeignKey(instance => instance.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Challenges.CompetitionChallenge>().WithMany()
            .HasForeignKey(instance => instance.CompetitionChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Challenges.Challenge>().WithMany()
            .HasForeignKey(instance => instance.ChallengeId).OnDelete(DeleteBehavior.Restrict);
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
