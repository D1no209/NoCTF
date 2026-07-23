using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Platform;

namespace NoCTF.Infrastructure.Persistence.Configurations.Platform;

internal sealed class DurableMaintenanceScheduleConfiguration
    : IEntityTypeConfiguration<DurableMaintenanceSchedule>
{
    public void Configure(EntityTypeBuilder<DurableMaintenanceSchedule> builder)
    {
        builder.ToTable("durable_maintenance_schedules");
        builder.Property(schedule => schedule.Kind).HasConversion<short>();

        // Data annotations cannot seed the singleton durable chain fence.
        builder.HasData(
            new DurableMaintenanceSchedule
            {
                Kind = MaintenanceChainKind.RunnerAssignmentReconciliation,
                ProcessingVersion = 1,
                UpdatedAt = DateTimeOffset.UnixEpoch
            },
            new DurableMaintenanceSchedule
            {
                Kind = MaintenanceChainKind.CompetitionLifecycle,
                ProcessingVersion = 1,
                UpdatedAt = DateTimeOffset.UnixEpoch
            },
            new DurableMaintenanceSchedule
            {
                Kind = MaintenanceChainKind.AwdCheckerDispatch,
                ProcessingVersion = 1,
                UpdatedAt = DateTimeOffset.UnixEpoch
            });
    }
}
