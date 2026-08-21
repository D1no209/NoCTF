using NoCTF.Worker;
using Wolverine;

namespace NoCTF.Tests.Unit.Worker;

public sealed class WorkerRoleTests
{
    [Test]
    public async Task Messaging_starts_singular_maintenance_assignment_promptly()
    {
        var options = new WolverineOptions();

        options.ConfigureNoCtfWorkerMessaging(durable: false);

        await Assert.That(options.Durability.CheckAssignmentPeriod)
            .IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(options.Durability.FirstHealthCheckExecution)
            .IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(options.Durability.ScheduledJobFirstExecution)
            .IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(options.Durability.ScheduledJobPollingTime)
            .IsEqualTo(TimeSpan.FromSeconds(1));
    }
}
