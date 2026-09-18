using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Messaging;

public sealed record ReleaseRunnerCapacity(RuntimeWorkloadIdentity Identity, string RunnerId);
