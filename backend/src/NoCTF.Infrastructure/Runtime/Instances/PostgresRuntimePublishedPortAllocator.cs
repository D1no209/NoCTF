using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Infrastructure.Runtime.Instances;

public sealed class PostgresRuntimePublishedPortAllocator(
    NoCtfDbContext db,
    RuntimePublishedPortRange range,
    ICompetitionEventRecorder? eventRecorder = null) : IRuntimePublishedPortAllocator
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<RuntimePublishedPortAllocationResult> AllocateAsync(
        RuntimeInstance instance,
        IReadOnlyList<RuntimePublishedPortTarget> targets,
        DateTimeOffset allocatedAt,
        CancellationToken cancellationToken)
    {
        var normalizedTargets = targets
            .Distinct()
            .OrderBy(target => target.ServiceName, StringComparer.Ordinal)
            .ThenBy(target => target.ContainerPort)
            .ToArray();
        if (instance.RuntimeProvider != RuntimeProvider.Docker)
            return new([]);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Published port allocation requires an active database transaction.");

        await using var allocationLease = await CriticalSectionCoordinator.AcquireAsync(
            db,
            "runtime-port-allocation",
            token => db.PlatformSettings.Where(settings => settings.Id == 1)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    settings => settings.CriticalSectionVersion,
                    settings => settings.CriticalSectionVersion + 1), token),
            cancellationToken);

        await db.RuntimeInstances
            .Where(owner => owner.Id == instance.Id)
            .Include(owner => owner.PublishedPorts)
            .LoadAsync(cancellationToken);
        if (instance.PublishedPorts.Count > 0)
            return Existing(instance.PublishedPorts, normalizedTargets);
        if (normalizedTargets.Length == 0)
            return new([]);

        var competitionHistory = await db.RuntimeInstances.AsNoTracking()
            .Where(owner => owner.CompetitionId == instance.CompetitionId)
            .SelectMany(owner => owner.PublishedPorts)
            .Select(port => port.HostPort)
            .ToListAsync(cancellationToken);
        var globallyBound = await db.RuntimeInstances.AsNoTracking()
            .Where(owner =>
                owner.State == RuntimeState.Queued
                || owner.State == RuntimeState.Provisioning
                || owner.State == RuntimeState.Running
                || owner.State == RuntimeState.Stopping
                || owner.State == RuntimeState.Failed && owner.ProviderReceiptJson != null)
            .SelectMany(owner => owner.PublishedPorts)
            .Select(port => port.HostPort)
            .ToListAsync(cancellationToken);
        var unavailable = competitionHistory
            .Concat(globallyBound)
            .ToHashSet();
        var available = Enumerable.Range(range.Start, range.Count)
            .Where(port => !unavailable.Contains(port))
            .ToList();
        if (available.Count < normalizedTargets.Length)
        {
            return new(
                [],
                RuntimePublishedPortAllocationFailure.RangeExhausted);
        }

        var mappings = new List<RuntimePublishedPortMapping>(normalizedTargets.Length);
        foreach (var target in normalizedTargets)
        {
            var index = RandomNumberGenerator.GetInt32(available.Count);
            var hostPort = available[index];
            available[index] = available[^1];
            available.RemoveAt(available.Count - 1);
            var publishedPort = new RuntimePublishedPort
            {
                Id = Guid.CreateVersion7(allocatedAt),
                RuntimeInstanceId = instance.Id,
                CompetitionId = instance.CompetitionId,
                ServiceName = target.ServiceName,
                ContainerPort = target.ContainerPort,
                HostPort = hostPort,
                AllocatedAt = allocatedAt
            };
            instance.PublishedPorts.Add(publishedPort);
            await events.RecordAsync(new(
                instance.CompetitionId,
                CompetitionEventKind.RuntimePortAllocated,
                CompetitionEventLevel.Information,
                instance.TeamId is null
                    ? CompetitionEventVisibility.Public
                    : CompetitionEventVisibility.Team,
                allocatedAt,
                TeamId: instance.TeamId,
                CompetitionChallengeId: instance.CompetitionChallengeId,
                RuntimeInstanceId: instance.Id,
                SubmissionId: instance.SubmissionId,
                RuntimeState: instance.State,
                RuntimeGeneration: instance.Generation,
                HostPort: publishedPort.HostPort), cancellationToken);
            mappings.Add(new(target.ServiceName, target.ContainerPort, hostPort));
        }
        return new(mappings);
    }

    private static RuntimePublishedPortAllocationResult Existing(
        IReadOnlyList<RuntimePublishedPort> existing,
        IReadOnlyList<RuntimePublishedPortTarget> targets)
    {
        var mappings = existing
            .OrderBy(port => port.ServiceName, StringComparer.Ordinal)
            .ThenBy(port => port.ContainerPort)
            .Select(port => new RuntimePublishedPortMapping(
                port.ServiceName,
                port.ContainerPort,
                port.HostPort))
            .ToArray();
        var matches = mappings.Length == targets.Count
            && mappings.Zip(targets).All(pair =>
                string.Equals(
                    pair.First.ServiceName,
                    pair.Second.ServiceName,
                    StringComparison.Ordinal)
                && pair.First.ContainerPort == pair.Second.ContainerPort);
        return matches
            ? new(mappings)
            : new([], RuntimePublishedPortAllocationFailure.TargetMismatch);
    }
}
