using NoCTF.Application.Messaging;

namespace NoCTF.Hosting;

public static class NatsSubjects
{
    public const string ControlStream = "NOCTF_V2_CONTROL";
    public const string GameplayStream = "NOCTF_V2_GAMEPLAY";
    public const string ProjectionStream = "NOCTF_V2_PROJECTION";
    public const string BackgroundStream = "NOCTF_V2_BACKGROUND";
    public const string WebhookStream = "NOCTF_V2_WEBHOOK";
    public const string EventsStream = "NOCTF_V2_EVENTS";
    public const string RunnerStream = "NOCTF_V2_RUNNER";
    public const string RealtimeEvents = "noctf.v2.events.realtime";
    public const string LeaderboardEvents = "noctf.v2.events.leaderboard";
    public const string WebhookEvents = "noctf.v2.events.webhook";

    public static string Subject(WorkerQueue queue) => queue switch
    {
        WorkerQueue.Control => "noctf.v2.control",
        WorkerQueue.Gameplay => "noctf.v2.gameplay",
        WorkerQueue.Projection => "noctf.v2.projection",
        WorkerQueue.Background => "noctf.v2.background",
        WorkerQueue.Webhook => "noctf.v2.webhook",
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

    public static string ScheduledSubject(WorkerQueue queue) =>
        $"{Subject(queue)}.scheduled";

    public static string Runner(string runnerId) =>
        $"noctf.v2.runner.{Normalize(runnerId)}";

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant().Replace('.', '-');
}
