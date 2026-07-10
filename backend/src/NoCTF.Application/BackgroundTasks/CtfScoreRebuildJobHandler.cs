using System.Text.Json;
using NoCTF.Core;
using NoCTF.Application.Scoring;

namespace NoCTF.Application.BackgroundTasks;

public sealed record CtfScoreRebuildPayload(Guid ChallengeId);

public sealed class CtfScoreRebuildJobHandler(ICtfScoreRebuilder rebuilder) : ICompetitionJobHandler
{
    public const string JobType = "ctf.score.rebuild";
    public string JobKey => JobType;

    public async Task ExecuteAsync(BackgroundTaskItem task, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Deserialize<CtfScoreRebuildPayload>(
                          task.PayloadJson,
                          new JsonSerializerOptions(JsonSerializerDefaults.Web))
                      ?? throw new InvalidOperationException("CTF score rebuild payload is invalid.");
        await rebuilder.RebuildChallengeAsync(task.CompetitionId, payload.ChallengeId, ct);
    }
}
