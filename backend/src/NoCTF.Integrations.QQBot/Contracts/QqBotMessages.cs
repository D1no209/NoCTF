namespace NoCTF.Integrations.QQBot.Contracts;

public sealed record CompetitionNotificationRequested(
    Guid CompetitionId,
    string EventType,
    string PayloadJson,
    string IdempotencyKey,
    DateTimeOffset RequestedAt);

public sealed record QqBotDeliveryResult(
    Guid CompetitionId,
    string IdempotencyKey,
    bool Delivered,
    string? Error);
