using JasperFx;
using JasperFx.CodeGeneration;
using System.Data.Common;
using System.Reflection;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;
using Wolverine.Attributes;

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

    private static readonly TimeSpan[] WebhookRetryDelays =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(8),
        TimeSpan.FromHours(24)
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
            WorkerQueue.Notifications => BackgroundRetryDelays,
            WorkerQueue.Webhook => WebhookRetryDelays,
            WorkerQueue.LiveSoloMedia => ControlRetryDelays,
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
        options.Policies.OnException<DbException>()
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
                chain.OnException(exception => NoCTF.Infrastructure.Persistence.TransactionFailureClassifier.IsRetryable(exception)
                        || NoCTF.Infrastructure.Persistence.TransactionFailureClassifier.IsDependencyUnavailable(exception)
                        || exception is NoCTF.Application.Scoring.Leaderboard.LeaderboardPublicationConcurrencyException,
                        "Known transient infrastructure failure")
                    .RetryWithCooldown(TimeSpan.FromMilliseconds(100))
                    .Then.ScheduleRetry(delays)
                    .WithFullJitter();
            }
        }

        private bool Matches(HandlerChain chain)
        {
            if (chain.MessageType != messageType)
                return false;

            // Single-destination policies must apply before listening endpoint
            // associations are populated by Wolverine.
            if (messageType != typeof(CompetitionEventCommitted))
                return true;

            return chain.Endpoints.Any(endpoint =>
                string.Equals(endpoint.EndpointName, endpointName, StringComparison.Ordinal)
                || string.Equals(
                    endpoint.Uri?.ToString().TrimEnd('/'),
                    NatsEndpointAddress(endpointName),
                    StringComparison.OrdinalIgnoreCase))
                || chain.Handlers.Any(handler =>
                    (handler.Method.GetCustomAttribute<StickyHandlerAttribute>()
                        ?? handler.HandlerType.GetCustomAttribute<StickyHandlerAttribute>())?.EndpointName == endpointName);
        }

        private static string NatsEndpointAddress(string endpointName) =>
            endpointName switch
            {
                CompetitionEventFanoutQueueNames.Realtime =>
                    "nats://subject/noctf.v2.events.realtime",
                CompetitionEventFanoutQueueNames.Leaderboard =>
                    "nats://subject/noctf.v2.events.leaderboard",
                CompetitionEventFanoutQueueNames.Webhook =>
                    "nats://subject/noctf.v2.events.webhook",
                WorkerQueueNames.Control => $"nats://subject/{NatsSubjects.Subject(WorkerQueue.Control)}",
                WorkerQueueNames.Gameplay => $"nats://subject/{NatsSubjects.Subject(WorkerQueue.Gameplay)}",
                WorkerQueueNames.Projection => $"nats://subject/{NatsSubjects.Subject(WorkerQueue.Projection)}",
                WorkerQueueNames.Background => $"nats://subject/{NatsSubjects.Subject(WorkerQueue.Background)}",
                WorkerQueueNames.Notifications => $"nats://subject/{NatsSubjects.Subject(WorkerQueue.Notifications)}",
                WorkerQueueNames.Webhook => $"nats://subject/{NatsSubjects.Subject(WorkerQueue.Webhook)}",
                WorkerQueueNames.LiveSoloMedia => $"nats://subject/{NatsSubjects.Subject(WorkerQueue.LiveSoloMedia)}",
                _ => throw new ArgumentOutOfRangeException(nameof(endpointName), endpointName, null)
            };
    }
}
