namespace NoCTF.Application.Messaging;

/// <summary>Rebuilds evaluation delivery from current relational facts without a persisted cursor.</summary>
public sealed record DispatchPendingGameplayFacts(DateTimeOffset At, Guid? AfterId = null);
