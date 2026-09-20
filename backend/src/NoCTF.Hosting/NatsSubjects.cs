using NoCTF.Application.Messaging;

namespace NoCTF.Hosting;

public static class NatsSubjects
{
    public const string ControlStream = "NOCTF_CONTROL";
    public const string GameplayStream = "NOCTF_GAMEPLAY";
    public const string ProjectionStream = "NOCTF_PROJECTION";
    public const string BackgroundStream = "NOCTF_BACKGROUND";
    public const string WebhookStream = "NOCTF_WEBHOOK";
    public const string EventsStream = "NOCTF_EVENTS";
    public const string RunnerStream = "NOCTF_RUNNER";
    public const string RealtimeEvents = "noctf.events.realtime";
    public const string LeaderboardEvents = "noctf.events.leaderboard";
    public const string WebhookEvents = "noctf.events.webhook";

    public static string Subject(WorkerQueue queue) => queue switch
    {
        WorkerQueue.Control => "noctf.control",
        WorkerQueue.Gameplay => "noctf.gameplay",
        WorkerQueue.Projection => "noctf.projection",
        WorkerQueue.Background => "noctf.background",
        WorkerQueue.Webhook => "noctf.webhook",
        _ => throw new ArgumentOutOfRangeException(nameof(queue), queue, null)
    };

    public static string Stream(WorkerQueue queue) => queue switch
    {
        WorkerQueue.Control => ControlStream,
        WorkerQueue.Gameplay => GameplayStream,
        WorkerQueue.Projection => ProjectionStream,
        WorkerQueue.Background => BackgroundStream,
        WorkerQueue.Webhook => WebhookStream,
        _ => throw new ArgumentOutOfRangeException(nameof(queue), queue, null)
    };

    public static string Runner(string runnerId) =>
        $"noctf.runner.{Normalize(runnerId)}";

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant().Replace('.', '-');
}
