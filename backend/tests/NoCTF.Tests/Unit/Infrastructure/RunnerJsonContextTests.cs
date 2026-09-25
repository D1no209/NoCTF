using System.Text.Json;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Infrastructure.Runtime.Capacity;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class RunnerJsonContextTests
{
    [Test]
    public async Task Source_generated_capacity_payloads_keep_the_Lua_wire_shape()
    {
        var rows = new[]
        {
            new RunnerRestoreRow("runner-claim:1", 1024, 1000, null, false, true)
        };
        var previous = new[]
        {
            new { key = "runner-claim:1", memory = 1024L, cpu = 1000L,
                pids = (long?)null, auxiliary = false, starting = true }
        };
        var json = JsonSerializer.Serialize(rows,
            RunnerCapacityRecoveryJsonContext.Default.RunnerRestoreRowArray);
        await Assert.That(json).IsEqualTo(JsonSerializer.Serialize(previous));

        var admission = new RunnerAdmissionSnapshot(default, null, null);
        await Assert.That(JsonSerializer.Serialize(admission,
                RunnerAdmissionJsonContext.Default.RunnerAdmissionSnapshot))
            .IsEqualTo(JsonSerializer.Serialize(admission));
    }
}
