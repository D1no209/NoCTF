using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Platform;

public enum MaintenanceChainKind : short
{
    RunnerAssignmentReconciliation,
    CompetitionLifecycle
}

public sealed class DurableMaintenanceSchedule
{
    [Key]
    public MaintenanceChainKind Kind { get; set; }

    [ConcurrencyCheck]
    public long ProcessingVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
