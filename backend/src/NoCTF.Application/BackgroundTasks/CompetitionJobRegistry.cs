using System.Collections.Frozen;
using NoCTF.Core;

namespace NoCTF.Application.BackgroundTasks;

public enum CompetitionJobWorkload
{
    Standard,
    LongRunning
}

public interface ICompetitionJobHandler
{
    string JobKey { get; }
    CompetitionJobWorkload Workload => CompetitionJobWorkload.Standard;
    Task ExecuteAsync(BackgroundTaskItem task, CancellationToken ct = default);
}

public interface ICompetitionJobRegistry
{
    IReadOnlyList<CompetitionJobDescriptor> Jobs { get; }
    ICompetitionJobHandler GetRequiredHandler(string jobKey);
}

public readonly record struct CompetitionJobDescriptor(
    string JobKey,
    CompetitionJobWorkload Workload);

public sealed class CompetitionJobRegistry : ICompetitionJobRegistry
{
    private readonly FrozenDictionary<string, ICompetitionJobHandler> _handlers;

    public CompetitionJobRegistry(IEnumerable<ICompetitionJobHandler> handlers)
    {
        var registrations = handlers
            .Select(handler => new
            {
                Handler = handler,
                JobKey = handler.JobKey?.Trim()
            })
            .ToArray();

        var invalid = registrations.FirstOrDefault(registration => string.IsNullOrWhiteSpace(registration.JobKey));
        if (invalid is not null)
        {
            throw new InvalidOperationException(
                $"Background task handler '{invalid.Handler.GetType().FullName}' registered an empty job key.");
        }

        var duplicate = registrations
            .GroupBy(registration => registration.JobKey!, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            var handlerNames = string.Join(
                ", ",
                duplicate.Select(registration => registration.Handler.GetType().FullName));
            throw new InvalidOperationException(
                $"Duplicate background task job key '{duplicate.Key}' is registered by: {handlerNames}.");
        }

        _handlers = registrations.ToFrozenDictionary(
            registration => registration.JobKey!,
            registration => registration.Handler,
            StringComparer.OrdinalIgnoreCase);
        Jobs = registrations
            .Select(registration => new CompetitionJobDescriptor(
                registration.JobKey!,
                registration.Handler.Workload))
            .ToArray();
    }

    public IReadOnlyList<CompetitionJobDescriptor> Jobs { get; }

    public ICompetitionJobHandler GetRequiredHandler(string jobKey)
    {
        if (!string.IsNullOrWhiteSpace(jobKey) && _handlers.TryGetValue(jobKey.Trim(), out var handler))
            return handler;

        throw new NotSupportedException($"Unknown background task type '{jobKey}'.");
    }
}
