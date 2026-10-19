using System.Globalization;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

if (args.Length is < 2 or > 3 || args.Length == 3 && args[2] != "--allocation-stacks")
    throw new ArgumentException("Expected: trace.nettrace output-directory [--allocation-stacks]");
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var stages = new Dictionary<(string Area, string Stage), List<double>>();
var byTrace = new Dictionary<string, List<StageObservation>>(StringComparer.Ordinal);
var requests = new List<RequestObservation>();
var providers = new Dictionary<string, long>(StringComparer.Ordinal);
var performanceEventKinds = new Dictionary<string, long>(StringComparer.Ordinal);
var allocations = new Dictionary<string, (long Ticks, long Bytes)>(StringComparer.Ordinal);
long eventCount = 0;
using var source = new EventPipeEventSource(args[0]);
source.Clr.GCAllocationTick += item =>
{
    var name = item.TypeName;
    var current = allocations.GetValueOrDefault(name);
    allocations[name] = (current.Ticks + 1, current.Bytes + item.AllocationAmount64);
};
using var events = new StreamWriter(Path.Combine(output, "performance-events.jsonl"));
source.Dynamic.All += item =>
{
    eventCount++;
    providers[item.ProviderName] = providers.GetValueOrDefault(item.ProviderName) + 1;
    if (item.ProviderName != "NoCTF-Performance") return;
    var kind = $"{(int)item.ID}:{item.EventName}";
    performanceEventKinds[kind] = performanceEventKinds.GetValueOrDefault(kind) + 1;
    if ((int)item.ID is not (1 or 2)) return;
    var traceId = (string)item.PayloadByName("traceId");
    if (traceId.Length != 0 && (traceId.Length != 32 || !traceId.All(char.IsAsciiHexDigit)))
        throw new InvalidDataException("Invalid performance TraceId.");
    var seconds = Convert.ToDouble(item.PayloadByName("seconds"), CultureInfo.InvariantCulture);
    if (!double.IsFinite(seconds) || seconds < 0) throw new InvalidDataException("Invalid performance duration.");
    if ((int)item.ID == 2)
    {
        var area = (string)item.PayloadByName("area");
        var stage = (string)item.PayloadByName("stage");
        var observation = new StageObservation(traceId, area, stage, seconds * 1000,
            item.TimeStamp.ToUniversalTime());
        if (!stages.TryGetValue((area, stage), out var values)) stages.Add((area, stage), values = []);
        values.Add(observation.DurationMs);
        if (traceId.Length != 0)
        {
            if (!byTrace.TryGetValue(traceId, out var entries)) byTrace.Add(traceId, entries = []);
            entries.Add(observation);
        }
        events.WriteLine(JsonSerializer.Serialize(observation));
    }
    else if ((int)item.ID == 1)
    {
        var request = new RequestObservation(traceId, (string)item.PayloadByName("endpoint"),
            (string)item.PayloadByName("outcome"), seconds * 1000, item.TimeStamp.ToUniversalTime());
        requests.Add(request);
        events.WriteLine(JsonSerializer.Serialize(request));
    }
};
source.Process();
var distributions = stages.Select(pair =>
{
    pair.Value.Sort();
    return new
    {
        area = pair.Key.Area, stage = pair.Key.Stage, count = pair.Value.Count,
        medianMs = Quantile(pair.Value, 0.5), p95Ms = Quantile(pair.Value, 0.95),
        p99Ms = Quantile(pair.Value, 0.99), maxMs = pair.Value[^1]
    };
}).OrderByDescending(item => item.p95Ms).ToArray();
var options = new JsonSerializerOptions { WriteIndented = true };
var realtimeRequests = requests.Where(IsRealtimeTransport).ToArray();
var operationRequests = requests.Where(request => !IsRealtimeTransport(request)).ToArray();
var operations = operationRequests.GroupBy(request => request.Endpoint).Select(group =>
{
    var rows = group.Select(request =>
    {
        var start = request.Utc.AddMilliseconds(-request.DurationMs);
        var database = byTrace.GetValueOrDefault(request.TraceId, [])
            .Where(stage => stage.Area == "Database" && stage.Utc >= start && stage.Utc <= request.Utc).ToArray();
        return new
        {
            request.DurationMs,
            Queries = (double)database.Count(stage => stage.Stage is "Read" or "Write" or "Failed"),
            QueryMs = database.Where(stage => stage.Stage is "Read" or "Write" or "Failed").Sum(stage => stage.DurationMs),
            ConnectionMs = database.Where(stage => stage.Stage == "ConnectionOpen").Sum(stage => stage.DurationMs)
        };
    }).ToArray();
    var queryCounts = rows.Select(row => row.Queries).Order().ToList();
    var queryTimes = rows.Select(row => row.QueryMs).Order().ToList();
    var connectionTimes = rows.Select(row => row.ConnectionMs).Order().ToList();
    return new
    {
        endpoint = group.Key, requests = rows.Length,
        queriesMean = queryCounts.Average(), queriesP95 = Quantile(queryCounts, 0.95),
        queryDurationP95Ms = Quantile(queryTimes, 0.95), connectionOpenP95Ms = Quantile(connectionTimes, 0.95),
        durationP95Ms = Quantile(rows.Select(row => row.DurationMs).Order().ToList(), 0.95)
    };
}).OrderByDescending(item => item.durationP95Ms).ToArray();
File.WriteAllText(Path.Combine(output, "trace-analysis.json"), JsonSerializer.Serialize(new
{
    parser = typeof(EventPipeEventSource).Assembly.GetName().Version?.ToString(),
    eventsLost = source.EventsLost, eventCount, requestCount = requests.Count, providers, performanceEventKinds,
    stages = distributions,
    sampledAllocations = allocations.Select(item => new { type = item.Key, ticks = item.Value.Ticks, sampledBytes = item.Value.Bytes })
        .OrderByDescending(item => item.sampledBytes).Take(50),
    operationRequestCount = operationRequests.Length,
    operations,
    realtimeTransportCount = realtimeRequests.Length,
    realtimeSessions = realtimeRequests.OrderByDescending(item => item.DurationMs).Take(50),
    slowRequests = operationRequests.OrderByDescending(item => item.DurationMs).Take(50).Select(request => new
    {
        request.TraceId, request.Endpoint, request.Outcome, request.DurationMs, request.Utc,
        stages = byTrace.GetValueOrDefault(request.TraceId, [])
    })
}, options));
Console.WriteLine($"Events={eventCount}; EventsLost={source.EventsLost}; Requests={requests.Count}; Stages={stages.Count}");
if (args.Length == 3)
{
    var indexed = TraceLog.CreateFromEventPipeDataFile(args[0], Path.Combine(output, "runtime.etlx"));
    using var log = new TraceLog(indexed);
    var paths = new Dictionary<(string Type, string Frames), (long Ticks, long Bytes)>();
    foreach (var item in log.Events)
    {
        if (item is not GCAllocationTickTraceData allocation) continue;
        var frames = new List<string>();
        for (var frame = allocation.CallStack(); frame is not null && frames.Count < 40; frame = frame.Caller)
            frames.Add(frame.CodeAddress.FullMethodName);
        var key = (allocation.TypeName, string.Join("\n", frames));
        var previous = paths.GetValueOrDefault(key);
        paths[key] = (previous.Ticks + 1, previous.Bytes + allocation.AllocationAmount64);
    }
    File.WriteAllText(Path.Combine(output, "allocation-callers.json"), JsonSerializer.Serialize(paths
        .Select(item => new { type = item.Key.Type, frames = item.Key.Frames.Split('\n'),
            ticks = item.Value.Ticks, sampledBytes = item.Value.Bytes })
        .OrderByDescending(item => item.sampledBytes).Take(100), options));
}

// Hub 传输请求记录的是长连接存续时间，单独保留，避免占据 HTTP 操作的慢请求清单。
static bool IsRealtimeTransport(RequestObservation request) =>
    request.Endpoint.StartsWith("/hubs/", StringComparison.Ordinal)
    && !request.Endpoint.EndsWith("/negotiate", StringComparison.Ordinal);

static double Quantile(List<double> sorted, double quantile)
{
    var position = (sorted.Count - 1) * quantile;
    var lower = (int)Math.Floor(position);
    var upper = (int)Math.Ceiling(position);
    return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
}

internal sealed record StageObservation(string TraceId, string Area, string Stage, double DurationMs, DateTime Utc);
internal sealed record RequestObservation(string TraceId, string Endpoint, string Outcome, double DurationMs, DateTime Utc);
