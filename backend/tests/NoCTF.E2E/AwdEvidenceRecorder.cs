using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NoCTF.E2E;

/// <summary>Opt-in read-only timeline capture for load investigations; never persists Flag values or credentials.</summary>
internal sealed class AwdEvidenceRecorder : IAsyncDisposable
{
    private static readonly string[] EvidenceFields =
    [
        "gameplayFactId", "competitionChallengeId", "teamId", "roundId", "health", "knownTargetFailure",
        "execution", "settlement", "reason", "connection", "functionality", "scheduledAt", "deadline",
        "receiptDeadline", "observedAt", "receivedAt", "adjudicatedAt", "attemptId", "attemptStartedAt",
        "attemptDeadline", "attempt", "finalScheduledCheck", "manifestComplete", "evidenceComplete",
        "requiredFlags", "readableFlags"
    ];
    private readonly HttpClient client;
    private readonly Guid competitionId;
    private readonly string? output;
    private readonly CancellationTokenSource stopping;
    private readonly Dictionary<Guid, string> fingerprints = [];
    private readonly Task loop;

    public AwdEvidenceRecorder(HttpClient client, Guid competitionId, CancellationToken ct)
    {
        this.client = client;
        this.competitionId = competitionId;
        stopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var directory = Environment.GetEnvironmentVariable("NOCTF_E2E_DIAGNOSTICS_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            output = Path.Combine(directory, $"awd-checks-{competitionId:N}.jsonl");
        }
        loop = output is null ? Task.CompletedTask : CaptureLoopAsync(stopping.Token);
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await CaptureAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                await AppendAsync(new { capturedAt = DateTimeOffset.UtcNow, errorType = exception.GetType().Name }, ct);
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private async Task CaptureAsync(CancellationToken ct)
    {
        for (var offset = 0; ; offset += 200)
        {
            var watch = Stopwatch.StartNew();
            using var response = await client.GetAsync($"/api/v1/admin/competitions/{competitionId}/awd/checks?AllSettlements=true&Offset={offset}&Limit=200", ct);
            response.EnsureSuccessStatusCode();
            var page = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!.AsObject();
            var items = page["items"]!.AsArray();
            await AppendAsync(new { capturedAt = DateTimeOffset.UtcNow, operation = "list", offset,
                durationMs = watch.Elapsed.TotalMilliseconds, count = items.Count, total = page["total"]!.GetValue<int>() }, ct);
            foreach (var item in items)
            {
                var id = item!["gameplayFactId"]!.GetValue<Guid>();
                var fingerprint = item.ToJsonString();
                if (fingerprints.TryGetValue(id, out var previous) && previous == fingerprint) continue;
                watch.Restart();
                using var detail = await client.GetAsync($"/api/v1/admin/competitions/{competitionId}/awd/checks/{id}", ct);
                detail.EnsureSuccessStatusCode();
                var source = JsonNode.Parse(await detail.Content.ReadAsStringAsync(ct))!.AsObject();
                var safe = new JsonObject();
                foreach (var field in EvidenceFields) safe[field] = source[field]?.DeepClone();
                safe["roundNumber"] = item["roundNumber"]?.DeepClone();
                safe["manifestSize"] = source["flags"]?.AsArray().Count;
                await AppendAsync(new { capturedAt = DateTimeOffset.UtcNow, operation = "evidence",
                    durationMs = watch.Elapsed.TotalMilliseconds, evidence = safe }, ct);
                fingerprints[id] = fingerprint;
            }
            if (offset + items.Count >= page["total"]!.GetValue<int>()) return;
        }
    }

    private Task AppendAsync<T>(T entry, CancellationToken ct) => File.AppendAllTextAsync(output!, JsonSerializer.Serialize(entry) + "\n", ct);

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        try { await loop; } catch (OperationCanceledException) { }
        if (output is not null)
        {
            using var final = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await CaptureAsync(final.Token); }
            catch (Exception exception)
            {
                await AppendAsync(new { capturedAt = DateTimeOffset.UtcNow, finalErrorType = exception.GetType().Name }, CancellationToken.None);
            }
        }
        stopping.Dispose();
    }
}
