using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Instances;

public sealed record ProvisionContainerRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    int Generation,
    string RunnerPool,
    ContainerRequest Definition);

public sealed record StopContainerRuntime(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    string RunnerPool,
    RuntimeProvider Provider,
    string ProviderReceiptJson);

public sealed record RuntimeProvisioned(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    string RunnerId,
    RuntimeProvider Provider,
    string ProviderReceiptJson,
    IReadOnlyList<string> Urls,
    IReadOnlyList<int> ParticipantUrlIndexes);

public sealed record RuntimeProvisionFailed(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    RuntimeFailureCode FailureCode);

public sealed record RuntimeStopped(Guid RuntimeInstanceId, long ProcessingVersion);
public sealed record RuntimeStopFailed(
    Guid RuntimeInstanceId,
    long ProcessingVersion,
    RuntimeFailureCode FailureCode);
