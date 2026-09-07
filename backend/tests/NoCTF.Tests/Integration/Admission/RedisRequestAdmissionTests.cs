using NoCTF.Application.Admission;
using NoCTF.Infrastructure.Admission;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Admission;

[Category("Integration")]
public sealed class RedisRequestAdmissionTests
{
    [Test, Timeout(120_000)]
    public async Task Independent_API_instances_share_account_limits_and_renewable_upload_slots(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await container.StartAsync(ct);
            await using var firstRedis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            await using var secondRedis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
            var first = new RedisRequestAdmission(firstRedis); var second = new RedisRequestAdmission(secondRedis);
            for (var i = 0; i < 20; i++)
                await using (await first.AcquireAsync([new("campus-ip", 100, 60), new($"account-{i}", 2, 60)], [], ct)) { }
            await using (await second.AcquireAsync([new("different-ip", 100, 60), new("account-0", 2, 60)], [], ct)) { }
            await ExpectFailure(() => second.AcquireAsync([new("rotated-ip", 100, 60), new("account-0", 2, 60)], [], ct), AdmissionFailure.RateLimited);
            await using (var lease = await first.AcquireAsync([], [new("patch-global", 1), new("target-1", 1)], ct))
            {
                await ExpectFailure(() => second.AcquireAsync([], [new("patch-global", 1)], ct), AdmissionFailure.CapacityBusy);
                await Task.Delay(TimeSpan.FromSeconds(32), ct); // Longer than the original 30-second lease.
                await Assert.That(lease.Token.IsCancellationRequested).IsFalse();
                await ExpectFailure(() => second.AcquireAsync([], [new("patch-global", 1)], ct), AdmissionFailure.CapacityBusy);
            }
            await using (await second.AcquireAsync([], [new("patch-global", 1)], ct)) { }
            await ExpectFailure(() => new RedisRequestAdmission().AcquireAsync([], [], ct), AdmissionFailure.DependencyUnavailable);
            await using var interrupted = await first.AcquireAsync([], [new("in-flight", 1)], ct);
            await container.StopAsync(ct);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, interrupted.Token).WaitAsync(TimeSpan.FromSeconds(25), ct); }
            catch (OperationCanceledException) when (interrupted.Token.IsCancellationRequested) { }
            await Assert.That(interrupted.Token.IsCancellationRequested).IsTrue();
            await ExpectFailure(() => second.AcquireAsync([], [], ct), AdmissionFailure.DependencyUnavailable);
        });
    }
    private static async Task ExpectFailure(Func<ValueTask<IRequestAdmissionLease>> action, AdmissionFailure failure)
    {
        AdmissionRejectedException? rejected = null;
        try { await using var lease = await action(); } catch (AdmissionRejectedException exception) { rejected = exception; }
        await Assert.That(rejected).IsNotNull();
        await Assert.That(rejected!.Failure).IsEqualTo(failure);
        await Assert.That(rejected.RetryAfterSeconds).IsGreaterThan(0);
    }
}
