using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;

namespace NoCTF.Infrastructure.Observability;

public sealed class PlatformLogBroadcastQueue
{
    public const string Subject = "noctf.v3.ui.platform-logs";
    private readonly Channel<PlatformLogView> channel = Channel.CreateBounded<PlatformLogView>(
        new BoundedChannelOptions(10_000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

    public bool TryWrite(PlatformLogView view)
    {
        if (channel.Writer.TryWrite(view)) return true;
        NoCtfTelemetry.RecordPlatformLogLiveDrop();
        return false;
    }
    public IAsyncEnumerable<PlatformLogView> ReadAllAsync(CancellationToken ct) =>
        channel.Reader.ReadAllAsync(ct);
}

public sealed class PlatformLogBroadcastAgent(
    PlatformLogBroadcastQueue queue,
    INatsConnection connection,
    ILogger<PlatformLogBroadcastAgent> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var view in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await connection.PublishAsync(PlatformLogBroadcastQueue.Subject,
                    data: JsonSerializer.SerializeToUtf8Bytes(view,
                        NoCtfWebMessageJsonContext.Default.PlatformLogView),
                    cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Platform log live notification failed.");
            }
        }
    }
}
