using Microsoft.Extensions.Configuration;
using NoCTF.Application.Messaging;

namespace NoCTF.Hosting;

public sealed class WorkerQueueOptions
{
    public IReadOnlyList<WorkerQueue> Enabled { get; set; } = WorkerQueueNames.All;

    public IReadOnlyDictionary<WorkerQueue, int> Concurrency { get; set; } =
        new Dictionary<WorkerQueue, int>();
}

public static class WorkerQueues
{
    public const string Control = WorkerQueueNames.Control;
    public const string Gameplay = WorkerQueueNames.Gameplay;
    public const string Projection = WorkerQueueNames.Projection;
    public const string Background = WorkerQueueNames.Background;
    public const string Webhook = WorkerQueueNames.Webhook;
    public const string LiveSoloMedia = WorkerQueueNames.LiveSoloMedia;

    public static IReadOnlyList<WorkerQueue> All => WorkerQueueNames.All;

    public static string GetName(WorkerQueue queue) => WorkerQueueNames.GetName(queue);

    public static IReadOnlyList<WorkerQueue> GetEnabled(IConfiguration configuration)
    {
        var section = configuration.GetSection("Worker:Queues");
        var configuredValues = section.GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
        if (configuredValues.Length == 0 && !string.IsNullOrWhiteSpace(section.Value))
        {
            configuredValues = section.Value
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (configuredValues.Length == 0
            || configuredValues.Any(value => value.Equals("all", StringComparison.OrdinalIgnoreCase)))
            return All;

        var queues = new List<WorkerQueue>(configuredValues.Length);
        foreach (var value in configuredValues)
        {
            if (!TryParse(value, out var queue))
            {
                throw new InvalidOperationException(
                    $"Unknown Worker queue '{value}'. Allowed values: control, gameplay, projection, background, webhook, livesolo-media, all.");
            }

            if (!queues.Contains(queue))
                queues.Add(queue);
        }

        if (queues.Count == 0)
            throw new InvalidOperationException("Worker:Queues must select at least one queue.");
        return queues;
    }

    public static int GetConcurrency(IConfiguration configuration, WorkerQueue queue)
    {
        var defaultValue = queue switch
        {
            WorkerQueue.Control => 2,
            WorkerQueue.Gameplay => 8,
            WorkerQueue.Projection => 2,
            WorkerQueue.Background => 2,
            WorkerQueue.Webhook => 32,
            WorkerQueue.LiveSoloMedia => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(queue), queue, null)
        };
        var configured = configuration[$"Worker:Concurrency:{queue}"];
        if (string.IsNullOrWhiteSpace(configured))
            return defaultValue;
        if (!int.TryParse(configured, out var concurrency) || concurrency is < 1 or > 128)
        {
            throw new InvalidOperationException(
                $"Worker:Concurrency:{queue} must be an integer between 1 and 128.");
        }

        return concurrency;
    }

    private static bool TryParse(string value, out WorkerQueue queue)
    {
        if (value.Equals("control", StringComparison.OrdinalIgnoreCase)
            || value.Equals(Control, StringComparison.OrdinalIgnoreCase))
        {
            queue = WorkerQueue.Control;
            return true;
        }

        if (value.Equals("gameplay", StringComparison.OrdinalIgnoreCase)
            || value.Equals(Gameplay, StringComparison.OrdinalIgnoreCase))
        {
            queue = WorkerQueue.Gameplay;
            return true;
        }

        if (value.Equals("projection", StringComparison.OrdinalIgnoreCase)
            || value.Equals(Projection, StringComparison.OrdinalIgnoreCase))
        {
            queue = WorkerQueue.Projection;
            return true;
        }

        if (value.Equals("background", StringComparison.OrdinalIgnoreCase)
            || value.Equals(Background, StringComparison.OrdinalIgnoreCase))
        {
            queue = WorkerQueue.Background;
            return true;
        }

        if (value.Equals("webhook", StringComparison.OrdinalIgnoreCase)
            || value.Equals(Webhook, StringComparison.OrdinalIgnoreCase))
        {
            queue = WorkerQueue.Webhook;
            return true;
        }

        if(value.Equals("livesolo-media",StringComparison.OrdinalIgnoreCase)||value.Equals(LiveSoloMedia,StringComparison.OrdinalIgnoreCase))
        {queue=WorkerQueue.LiveSoloMedia;return true;}
        queue = default;
        return false;
    }
}
