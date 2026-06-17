using System.Text.Json;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Core;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWDP;

public class AwdpPatchValidationJobHandler(IAwdpPatchService patchService) : ICompetitionJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string JobKey => BackgroundTaskTypes.AwdpPatchValidation;

    public async Task ExecuteAsync(BackgroundTaskItem task, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Deserialize<AwdpPatchValidationPayload>(task.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Invalid AWDP patch validation payload.");

        await patchService.ValidatePatchAsync(payload.SubmissionId, ct);
    }
}
