using Microsoft.Extensions.DependencyInjection;
using NoCTF.Core;

namespace NoCTF.Application.BackgroundTasks;

public interface ICompetitionJobHandler
{
    string JobKey { get; }
    Task ExecuteAsync(BackgroundTaskItem task, CancellationToken ct = default);
}

public interface ICompetitionJobRegistry
{
    ICompetitionJobHandler GetRequiredHandler(string jobKey);
}

public class CompetitionJobRegistry(IServiceProvider serviceProvider) : ICompetitionJobRegistry
{
    public ICompetitionJobHandler GetRequiredHandler(string jobKey)
    {
        var handler = serviceProvider
            .GetServices<ICompetitionJobHandler>()
            .FirstOrDefault(h => string.Equals(h.JobKey, jobKey, StringComparison.OrdinalIgnoreCase));

        return handler ?? throw new NotSupportedException($"Unknown background task type '{jobKey}'.");
    }
}
