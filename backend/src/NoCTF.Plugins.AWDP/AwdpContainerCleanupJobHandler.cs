using System.Text.Json;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Core;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWDP;

public sealed class AwdpContainerCleanupJobHandler(IContainerManager containerManager) : ICompetitionJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string JobKey => AwdpBackgroundTaskTypes.ContainerCleanup;

    public async Task ExecuteAsync(BackgroundTaskItem task, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Deserialize<AwdpContainerCleanupPayload>(task.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Invalid AWDP container cleanup payload.");
        await containerManager.DestroyContainerAsync(payload.Container, ct);
    }
}
