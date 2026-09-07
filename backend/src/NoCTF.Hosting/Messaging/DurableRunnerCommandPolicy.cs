using Wolverine.Configuration;
using Wolverine.Nats.Internal;
using Wolverine.Runtime;

namespace NoCTF.Hosting.Messaging;

/// <summary>Dynamic Runner destinations must participate in the same PostgreSQL Outbox as static business queues.</summary>
public sealed class DurableRunnerCommandPolicy : IEndpointPolicy
{
    public void Apply(Endpoint endpoint, IWolverineRuntime runtime)
    {
        if (endpoint is not NatsEndpoint nats || endpoint.IsListener || endpoint.IsUsedForReplies
            || !nats.Subject.StartsWith("noctf.runner.", StringComparison.Ordinal)) return;
        nats.UseJetStream = true;
        nats.StreamName = NatsSubjects.RunnerStream;
        nats.Mode = EndpointMode.Durable;
    }
}
