namespace NoCTF.Application.Messaging;

public enum WorkerQueue
{
    Control,
    Gameplay,
    Projection,
    Background
}

public static class WorkerQueueNames
{
    public const string Control = "noctf-control";
    public const string Gameplay = "noctf-gameplay";
    public const string Projection = "noctf-projection";
    public const string Background = "noctf-background";

    public static readonly IReadOnlyList<WorkerQueue> All =
    [
        WorkerQueue.Control,
        WorkerQueue.Gameplay,
        WorkerQueue.Projection,
        WorkerQueue.Background
    ];

    public static string GetName(WorkerQueue queue) => queue switch
    {
        WorkerQueue.Control => Control,
        WorkerQueue.Gameplay => Gameplay,
        WorkerQueue.Projection => Projection,
        WorkerQueue.Background => Background,
        _ => throw new ArgumentOutOfRangeException(nameof(queue), queue, null)
    };
}

public static class CompetitionEventFanoutQueueNames
{
    public const string Realtime = "noctf-competition-events-realtime";
    public const string Leaderboard = "noctf-competition-events-leaderboard";

    public static readonly IReadOnlyList<string> All = [Realtime, Leaderboard];
}
