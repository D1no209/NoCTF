using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace NoCTF.Tests.Unit.Hosting;

public sealed class ManagedRuntimeMemoryTests
{
    [Test]
    public async Task Large_objects_keep_their_contents_across_threads_and_collections()
    {
        var channel = Channel.CreateBounded<byte[]>(4);
        var producer = Task.Run(async () =>
        {
            for (var index = 0; index < 64; index++)
            {
                var data = new byte[128 * 1024];
                Array.Fill(data, (byte)index);
                await channel.Writer.WriteAsync(data);
            }
            channel.Writer.Complete();
        });
        var received = 0;
        await foreach (var data in channel.Reader.ReadAllAsync())
        {
            if (received % 8 == 0) GC.Collect();
            await Assert.That(data.All(value => value == received)).IsTrue();
            received++;
        }
        await producer;
        await Assert.That(received).IsEqualTo(64);
    }

    [Test]
    public async Task Pinned_buffer_stays_valid_during_collection_and_native_memory_copy()
    {
        var buffer = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        var native = Marshal.AllocHGlobal(buffer.Length);
        try
        {
            var address = pinned.AddrOfPinnedObject();
            for (var pass = 0; pass < 8; pass++)
            {
                GC.Collect();
                await Assert.That(pinned.AddrOfPinnedObject()).IsEqualTo(address);
                Marshal.Copy(buffer, 0, native, buffer.Length);
                var roundTrip = new byte[buffer.Length];
                Marshal.Copy(native, roundTrip, 0, roundTrip.Length);
                await Assert.That(roundTrip.SequenceEqual(buffer)).IsTrue();
                await Assert.That(Marshal.ReadByte(address, 250)).IsEqualTo((byte)250);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(native);
            pinned.Free();
        }
    }

    [Test]
    public async Task Unreachable_objects_are_finalized_and_weak_references_clear()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var weak = CreateFinalizable(completed);
        for (var pass = 0; pass < 8 && !completed.Task.IsCompleted; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        GC.Collect();
        await Assert.That(weak.IsAlive).IsFalse();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateFinalizable(TaskCompletionSource completed)
        => new(new Finalizable(completed));

    private sealed class Finalizable(TaskCompletionSource completed)
    {
        ~Finalizable() => completed.TrySetResult();
    }
}
