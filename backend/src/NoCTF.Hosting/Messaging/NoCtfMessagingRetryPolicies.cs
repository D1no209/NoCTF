using JasperFx;
using JasperFx.CodeGeneration;
using Npgsql;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace NoCTF.Hosting.Messaging;

public static class NoCtfMessagingRetryPolicies
{
    private static readonly TimeSpan[] ControlRetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    private static readonly TimeSpan[] GameplayRetryDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15)
    };

    private static readonly TimeSpan[] ProjectionRetryDelays =
    {
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    };

    private static readonly TimeSpan[] BackgroundRetryDelays =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2)
    };

    private static readonly TimeSpan[] RunnerRetryDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(1)
    };

    public static void ConfigureNoCtfInfrastructureRetriesFor<TMessage>(
        this WolverineOptions options,
        WorkerQueue queue,
        string endpointName)
    {
        var delays = queue switch
        {
            WorkerQueue.Control => ControlRetryDelays,
            WorkerQueue.Gameplay => GameplayRetryDelays,
            WorkerQueue.Projection => ProjectionRetryDelays,
            WorkerQueue.Background => BackgroundRetryDelays,
            _ => throw new ArgumentOutOfRangeException(nameof(queue), queue, null)
        };

        options.Policies.Add(new NoCtfInfrastructureRetryPolicy(
            typeof(TMessage),
            endpointName,
            delays));
    }

    public static void ConfigureNoCtfRunnerInfrastructureRetries(this WolverineOptions options)
    {
        options.Policies.OnException<TimeoutException>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(250))
            .Then.ScheduleRetry(RunnerRetryDelays)
            .WithFullJitter();
        options.Policies.OnException<NpgsqlException>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(250))
            .Then.ScheduleRetry(RunnerRetryDelays)
            .WithFullJitter();
    }

    private sealed class NoCtfInfrastructureRetryPolicy(
        Type messageType,
        string endpointName,
        TimeSpan[] delays) : IHandlerPolicy
    {
        public void Apply(
            IReadOnlyList<HandlerChain> chains,
            GenerationRules rules,
            IServiceContainer container)
        {
            foreach (var chain in chains.Where(Matches))
            {
                if (messageType == typeof(CleanupFile)
                    || messageType == typeof(InvalidateDeletedCompetitionReadModels))
                {
                    // Deletion has already committed. Storage/Redis/IO failures must retry
                    // independently and eventually remain visible in the durable error queue.
                    chain.OnException<Exception>()
                        .RetryWithCooldown(TimeSpan.FromMilliseconds(250))
                        .Then.ScheduleRetry(delays)
                        .WithFullJitter();
                    continue;
                }
                chain.OnException<TimeoutException>()
                    .RetryWithCooldown(TimeSpan.FromMilliseconds(250))
                    .Then.ScheduleRetry(delays)
                    .WithFullJitter();
                chain.OnException<NpgsqlException>()
                    .RetryWithCooldown(TimeSpan.FromMilliseconds(250))
                    .Then.ScheduleRetry(delays)
                    .WithFullJitter();
            }
        }

        private bool Matches(HandlerChain chain)
        {
            if (chain.MessageType != messageType)
                return false;

            // These single-consumer messages have one handler and one background destination.
            // Handler policies may run before endpoint associations are populated.
            if (messageType == typeof(CleanupFile)
                || messageType == typeof(InvalidateDeletedCompetitionReadModels))
                return true;

            return chain.Endpoints.Any(endpoint =>
                string.Equals(endpoint.EndpointName, endpointName, StringComparison.Ordinal)
                || string.Equals(
                    endpoint.Uri?.ToString().TrimEnd('/'),
                    NatsEndpointAddress(endpointName),
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string NatsEndpointAddress(string endpointName) =>
            endpointName switch
            {
                CompetitionEventFanoutQueueNames.Realtime =>
                    "nats://subject/noctf.events.realtime",
                CompetitionEventFanoutQueueNames.Leaderboard =>
                    "nats://subject/noctf.events.leaderboard",
                _ => $"nats://subject/noctf.{endpointName.Replace("-", ".", StringComparison.Ordinal)}"
            };
    }
}
