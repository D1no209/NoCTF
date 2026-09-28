using NoCTF.Domain.Runtime;
using NoCTF.Hosting.Health;

namespace NoCTF.Runner.Composition;

public sealed class RunnerAdmissionReadinessDependency(RunnerResourceObserver observer) : IReadinessDependency
{
    public string Name => "runner-admission";
    public bool FailureIsCritical => observer.Current.State != RunnerAdmissionState.PressureBlocked;
    public Task CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        observer.EnsureFreshAdmission();
        return Task.CompletedTask;
    }
}
