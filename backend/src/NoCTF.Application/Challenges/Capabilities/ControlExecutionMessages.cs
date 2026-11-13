using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Application.Messaging;

public sealed record RunControlObservation(Guid ExecutionId, Guid RuntimeInstanceId,
    NoCTF.Application.Challenges.Capabilities.CapabilityExecutionScope Scope, DateTimeOffset Deadline,
    string RunnerId) : IRunnerNodeMessage;
public sealed record FinalizeControlObservation(Guid ExecutionId, Guid RuntimeInstanceId,
    NoCTF.Application.Challenges.Capabilities.CapabilityExecutionScope Scope);
public sealed record RecoverControlObservations;
