using System.Diagnostics.Tracing;

namespace NoCTF.Application.Observability;

[EventSource(Name = "NoCTF-Performance")]
public sealed class PerformanceEvents : EventSource
{
    public static readonly PerformanceEvents Log = new();

    private PerformanceEvents() { }

    [Event(1, Level = EventLevel.Informational)]
    public void RequestCompleted(string traceId, string endpoint, string outcome, double seconds)
    {
        if (IsEnabled()) WriteEvent(1, traceId, endpoint, outcome, seconds);
    }

    [Event(2, Level = EventLevel.Informational)]
    public void StageCompleted(string traceId, string area, string stage, double seconds)
    {
        if (IsEnabled()) WriteEvent(2, traceId, area, stage, seconds);
    }
}
