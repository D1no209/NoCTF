using Wolverine.Configuration;
using Wolverine.Nats.Internal;
using Wolverine.Runtime;

namespace NoCTF.Hosting.Messaging;

/// <summary>Dynamic Runner destinations use the same durable JetStream transport as static business queues.</summary>
public sealed class DurableRunnerCommandPolicy : IEndpointPolicy
{
    public void Apply(Endpoint endpoint, IWolverineRuntime runtime)
    {
        if (endpoint is not NatsEndpoint nats || endpoint.IsListener || endpoint.IsUsedForReplies
            || !nats.Subject.StartsWith("noctf.v2.runner.", StringComparison.Ordinal)) return;
        nats.UseJetStream = true;
        nats.StreamName = NatsSubjects.RunnerStream;
        nats.Mode = EndpointMode.Durable;
    }
}
