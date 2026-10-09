using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NoCTF.Hosting;
using Wolverine;
using Wolverine.Nats;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration"),NotInParallel]
public sealed class LiveSoloMediaQueueIsolationTests
{
    [Test,Timeout(300_000)]
    public async Task A_blocked_media_export_does_not_block_a_durable_round_wakeup(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async()=>{
            await using var nats=new ContainerBuilder("nats:2.12.2-alpine")
                .WithPortBinding(4222,true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await nats.StartAsync(ct);var probe=new Probe();
            var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["ConnectionStrings:Nats"]=$"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}"}).Build();
            using var host=Host.CreateDefaultBuilder().ConfigureServices(s=>s.AddSingleton(probe))
                .UseWolverine(options=>{
                    options.Discovery.DisableConventionalDiscovery();options.Discovery.IncludeType(typeof(MediaHandler));options.Discovery.IncludeType(typeof(RoundHandler));
                    options.ConfigureNoCtfPersistence(configuration,HostRoles.Only(HostRole.Worker));
                    options.ConfigureNoCtfMessageRouting(configuration,HostRoles.Only(HostRole.Worker));
                    foreach(var queue in new[]{WorkerQueue.LiveSoloMedia,WorkerQueue.Control})
                        options.ListenToNatsSubject(NatsSubjects.Subject(queue)).UseJetStream(NatsSubjects.Stream(queue),WorkerQueues.GetName(queue))
                            .Named(WorkerQueues.GetName(queue)).MaximumParallelMessages(1);
                }).Build();
            await host.StartAsync(ct);
            try
            {
                var bus=host.Services.GetRequiredService<IMessageBus>();await bus.PublishAsync(new AdvanceLiveSoloCapture(Guid.NewGuid()));
                await probe.MediaStarted.Task.WaitAsync(TimeSpan.FromSeconds(20),ct);
                var wakeup=new AdvanceLiveSoloRound(Guid.NewGuid(),1,DateTimeOffset.UtcNow);await bus.PublishAsync(wakeup);
                await Assert.That(await probe.RoundReceived.Task.WaitAsync(TimeSpan.FromSeconds(5),ct)).IsEqualTo(wakeup);
                await Assert.That(probe.ReleaseMedia.Task.IsCompleted).IsFalse();
            }
            finally{probe.ReleaseMedia.TrySetResult();await host.StopAsync(ct);}
        });
    }
    public sealed class Probe
    {
        public TaskCompletionSource MediaStarted {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseMedia {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AdvanceLiveSoloRound> RoundReceived {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class MediaHandler(Probe probe)
    {
        public async Task Handle(AdvanceLiveSoloCapture message,CancellationToken ct)
        {probe.MediaStarted.TrySetResult();await probe.ReleaseMedia.Task.WaitAsync(ct);}
    }
    public sealed class RoundHandler(Probe probe)
    {
        public void Handle(AdvanceLiveSoloRound message)=>probe.RoundReceived.TrySetResult(message);
    }
}
