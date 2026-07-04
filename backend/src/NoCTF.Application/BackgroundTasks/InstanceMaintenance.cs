namespace NoCTF.Application.BackgroundTasks;

public interface IInstanceMaintenanceService
{
    string Name { get; }
    Task<InstanceMaintenanceResult> MaintainAsync(CancellationToken ct = default);
}

public sealed record InstanceMaintenanceResult(
    int ExpiredInstances,
    int SyncedInstances,
    int FailedInstances);
