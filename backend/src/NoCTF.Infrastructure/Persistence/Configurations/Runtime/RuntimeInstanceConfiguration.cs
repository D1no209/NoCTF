using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Persistence.Configurations.Runtime;

internal sealed class RuntimeInstanceConfiguration : IEntityTypeConfiguration<RuntimeInstance>
{
    public void Configure(EntityTypeBuilder<RuntimeInstance> builder)
    {
        builder.ToTable("runtime_instances");
        builder.HasKey(instance => instance.Id);
        builder.Property(instance => instance.RuntimeKind).HasConversion<short>();
        builder.HasDiscriminator(instance => instance.Purpose)
            .HasValue<PlayerRuntimeInstance>(RuntimePurpose.Player)
            .HasValue<AwdpTargetRuntimeInstance>(RuntimePurpose.AwdpTarget)
            .HasValue<PracticeRuntimeInstance>(RuntimePurpose.Practice)
            .HasValue<AwdpAttackRuntimeInstance>(RuntimePurpose.AwdpAttack)
            .HasValue<TemplateTestRuntimeInstance>(RuntimePurpose.TemplateTest)
            .HasValue<PatchVerificationTargetRuntimeInstance>(RuntimePurpose.PatchVerificationTarget);
        builder.Property(instance => instance.AccessMode).HasConversion<short>();
        builder.Property(instance => instance.TestFlagDelivery).HasConversion<short>();
        builder.Property(instance => instance.TestFlagState).HasConversion<short>();
        builder.Property(instance => instance.RuntimeProvider).HasConversion<short>();
        builder.Property(instance => instance.State).HasConversion<short>();
        builder.Property(instance => instance.FailureCode).HasConversion<short>();
        builder.HasOne(instance => instance.ProviderReceipt)
            .WithOne()
            .HasForeignKey<RuntimeReceipt>(receipt => receipt.RuntimeInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(instance => instance.ProviderReceipt).AutoInclude();
        builder.HasOne(instance => instance.ActiveSlot)
            .WithOne()
            .HasForeignKey<ActiveRuntimeSlot>(slot => slot.RuntimeInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(instance => instance.ActiveSlot).AutoInclude();
        builder.OwnsMany(instance => instance.CapacityAllocationEntries, allocations =>
        {
            allocations.ToTable("runtime_capacity_allocations");
            allocations.WithOwner().HasForeignKey("runtime_instance_id");
            allocations.HasKey(entry => entry.Id);
            allocations.HasIndex(entry => new
            {
                entry.WorkloadKind,
                entry.WorkloadRuntimeInstanceId,
                entry.OperationId
            }).IsUnique();
        });
        // Access endpoints are ordered owned rows so the model remains relational and provider-neutral.
        builder.OwnsMany(instance => instance.AccessEndpoints, endpoints =>
        {
            endpoints.ToTable("runtime_access_endpoints");
            endpoints.WithOwner().HasForeignKey("runtime_instance_id");
            endpoints.HasKey("runtime_instance_id", nameof(RuntimeAccessEndpoint.BindingIndex));
            endpoints.Property(endpoint => endpoint.BindingIndex).ValueGeneratedNever();
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
        });
        builder.HasIndex(instance => instance.ChallengeId);
        builder.HasIndex(instance => instance.GameplayFactId);
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
            ports.ToTable("runtime_published_ports");
            ports.WithOwner().HasForeignKey("runtime_instance_id");
            ports.HasKey(port => port.Id);
            ports.Property(port => port.ServiceName).HasMaxLength(63);
        });
    }
}

internal sealed class ActiveRuntimeSlotConfiguration
    : IEntityTypeConfiguration<ActiveRuntimeSlot>
{
    public void Configure(EntityTypeBuilder<ActiveRuntimeSlot> builder)
    {
        builder.ToTable("active_runtime_slots");
        builder.HasKey(slot => slot.Key);
        builder.Property(slot => slot.Key).HasMaxLength(160);
        builder.HasIndex(slot => slot.RuntimeInstanceId).IsUnique();
    }
}
