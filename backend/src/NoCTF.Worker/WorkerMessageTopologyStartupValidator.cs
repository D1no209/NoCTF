using JasperFx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Hosting;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace NoCTF.Worker;

public sealed class WorkerMessageTopologyStartupValidator(
    IWolverineRuntime runtime,
    IOptions<WorkerQueueOptions> queueOptions) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        var expectedQueues = ExpectedFanoutQueues(queueOptions.Value.Enabled);
        await runtime.AllRegisteredListenersAsync(cancellationToken);
        var routing = runtime.ExplainRoutingFor(typeof(CompetitionEventCommitted)).ToText();

        ValidateFanoutRouting(expectedQueues, routing);
        ValidateFanoutEndpoints(expectedQueues);
        ValidateFanoutHandlers(expectedQueues);
    }

    private void ValidateFanoutEndpoints(IReadOnlyList<string> expectedQueues)
    {
        if (expectedQueues.Distinct(StringComparer.Ordinal).Count() != expectedQueues.Count)
        {
            throw new InvalidOperationException(
                "Competition event fan-out endpoint names must be unique.");
        }

        foreach (var queueName in expectedQueues)
        {
            Endpoint? endpoint;
            try
            {
                endpoint = runtime.Endpoints.EndpointFor(
                    new Uri(NatsEndpointAddress(queueName), UriKind.Absolute));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Required fan-out endpoint '{queueName}' is not registered.",
                    exception);
            }

            if (endpoint is null)
            {
                throw new InvalidOperationException(
                    $"Required fan-out endpoint '{queueName}' is not registered.");
            }
            if (!endpoint.IsListener
                || endpoint.Uri?.Scheme != "nats"
                || !string.Equals(endpoint.BrokerRole, "stream", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Fan-out endpoint '{queueName}' must be a durable NATS JetStream listener "
                    + $"(listener={endpoint.IsListener}, uri={endpoint.Uri}, "
                    + $"brokerRole={endpoint.BrokerRole}, mode={endpoint.Mode}).");
            }

            if (string.Equals(
                    queueName,
                    CompetitionEventFanoutQueueNames.Leaderboard,
                    StringComparison.Ordinal))
            {
                ValidateLeaderboardListenerScope(endpoint.ListenerScope);
            }
        }
    }

    internal static void ValidateLeaderboardListenerScope(ListenerScope listenerScope)
    {
        if (listenerScope != ListenerScope.PinnedToLeader)
        {
            throw new InvalidOperationException(
                "Leaderboard fan-out must be pinned to the Wolverine leader so its process-local merge queue is owned by the active Singular Agent.");
        }
    }

    private void ValidateFanoutHandlers(IReadOnlyList<string> expectedQueues)
    {
        var graph = runtime.Services.GetRequiredService<HandlerGraph>();
        var root = graph.ChainFor(typeof(CompetitionEventCommitted))
            ?? throw new InvalidOperationException(
                "Competition event fan-out handler graph is not registered.");
        var chains = root.ByEndpoint;
        var handlerTypes = new HashSet<Type>();

        foreach (var queueName in expectedQueues)
        {
            var endpointAddress = NatsEndpointAddress(queueName);
            var matches = chains
                .Where(chain => chain.Endpoints.Any(endpoint =>
                    string.Equals(endpoint.EndpointName, queueName, StringComparison.Ordinal)
                    || string.Equals(
                        endpoint.Uri?.ToString().TrimEnd('/'),
                        endpointAddress,
                        StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Fan-out endpoint '{queueName}' must own exactly one Sticky handler chain.");
            }

            var calls = matches[0].HandlerCalls();
            if (calls.Length != 1 || !handlerTypes.Add(calls[0].HandlerType))
            {
                throw new InvalidOperationException(
                    $"Fan-out endpoint '{queueName}' must own one unique Sticky handler.");
            }
        }
    }

    internal static void ValidateFanoutRouting(
        IReadOnlyList<string> expectedQueues,
        string routing)
    {
        foreach (var queueName in expectedQueues)
        {
            var endpoint = NatsEndpointAddress(queueName);
            if (!routing.Contains(endpoint, StringComparison.OrdinalIgnoreCase)
                || routing.Contains($"local://{queueName}", StringComparison.OrdinalIgnoreCase)
                || routing.Contains(
                    $"local://{queueName.Replace('-', '_')}",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Competition event fan-out destination '{queueName}' is not routed to NATS.");
            }
        }
    }

    internal static string NatsEndpointAddress(string queueName) =>
        $"nats://subject/noctf.events.{(queueName.Contains("leaderboard", StringComparison.Ordinal)
            ? "leaderboard"
            : "realtime")}";

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static IReadOnlyList<string> ExpectedFanoutQueues(
        IReadOnlyCollection<WorkerQueue> enabled)
    {
        var queues = new List<string>(2);
        if (enabled.Contains(WorkerQueue.Background))
            queues.Add(CompetitionEventFanoutQueueNames.Realtime);
        if (enabled.Contains(WorkerQueue.Projection))
            queues.Add(CompetitionEventFanoutQueueNames.Leaderboard);
        return queues;
    }
}
