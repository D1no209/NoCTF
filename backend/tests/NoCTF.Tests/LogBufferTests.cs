using NoCTF.API.Logging;
using NoCTF.API.SignalR;

namespace NoCTF.Tests;

public class LogBufferTests
{
    [Fact]
    public async Task Enqueue_UsesOneBoundedConsumerAndShedsOverload()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maxActive = 0;
        await using var buffer = new LogBuffer(
            broadcastCapacity: 2,
            async (_, ct) =>
            {
                var current = Interlocked.Increment(ref active);
                InterlockedExtensions.Max(ref maxActive, current);
                entered.TrySetResult();
                try
                {
                    await release.Task.WaitAsync(ct);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });

        buffer.Enqueue(Entry(0));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var i = 1; i <= 20; i++)
            buffer.Enqueue(Entry(i));

        Assert.True(buffer.DroppedBroadcastCount >= 18);
        Assert.Equal("message-20", buffer.GetRecent(1).Single().Message);
        Assert.Equal(1, maxActive);
        release.TrySetResult();
    }

    private static LogEntryDto Entry(int index)
        => new("Information", $"message-{index}", "test", DateTimeOffset.UtcNow);
}

file static class InterlockedExtensions
{
    public static void Max(ref int location, int value)
    {
        var current = Volatile.Read(ref location);
        while (current < value)
        {
            var observed = Interlocked.CompareExchange(ref location, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }
}
