namespace NoCTF.Application.Messaging;

public enum MessageExecutionOutcome
{
    Applied,
    Idempotent,
    Superseded,
    DeferredCapacity,
    RejectedBusiness,
    Conflict
}
