using NoCTF.Application.Common;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Domain.Competitions.StaffWebhooks;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;

namespace NoCTF.Application.Competitions.StaffWebhooks;

public sealed record StaffWebhookTargetView(Guid Id, string Name, string? EndpointUrl, bool Enabled,
    bool AuthorizationRevoked, IReadOnlyList<StaffWorkItemKind> Categories, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, DateTimeOffset? PreviousSecretValidUntil, bool Synchronizing, DateTimeOffset? FailureSince);
public sealed record StaffWebhookTargetPage(IReadOnlyList<StaffWebhookTargetView> Items, int Total, bool CanManage);
public sealed record StaffWebhookMutation(StaffWebhookTargetView Target, string? SigningSecret = null);
public sealed record SaveStaffWebhook(Guid CompetitionId, Guid ActorId, Guid? TargetId, string Name, string EndpointUrl,
    bool Enabled, IReadOnlyList<StaffWorkItemKind> Categories);
public sealed record DeliverStaffWebhook(Guid CompetitionId, Guid EventId, Guid TargetId, Guid AttemptToken);
public sealed record StaffWebhookDeliveryView(Guid EventId, Guid TargetId, StaffWebhookEventKind Kind, long Sequence,
    StaffWebhookDeliveryState State, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, DateTimeOffset NextAttemptAt,
    int Attempts, int? LastStatusCode);
public sealed record StaffWebhookDeliveryPage(IReadOnlyList<StaffWebhookDeliveryView> Items, int Total);
public sealed record StaffWebhookPreparedDelivery(CompetitionWebhookDelivery? Delivery, bool Deferred = false);
public sealed record StaffWorkItemView(StaffWorkItemKind Kind, Guid Id, CheatIncidentStatus? CheatStatus,
    CompetitionQuestionStatus? ConsultationStatus, TeamBanAppealStatus? AppealStatus, bool RequiresStaffAction,
    DateTimeOffset? ActionRequiredSince, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    Guid? TeamId, string? TeamName, Guid? ChallengeId, string? ChallengeTitle, GameplayFactFailureCode? ReasonCode, string ManagementUrl);
public sealed record StaffWorkItemPage(IReadOnlyList<StaffWorkItemView> Items, int Total);

public interface IStaffWebhookStore
{
    Task<OperationResult<StaffWebhookTargetPage, StaffWebhookFailure>> ListAsync(Guid competitionId, Guid actorId, int offset, int limit, CancellationToken ct, bool descending = true);
    Task<OperationResult<StaffWebhookMutation, StaffWebhookFailure>> SaveAsync(SaveStaffWebhook command, CancellationToken ct);
    Task<OperationResult<StaffWebhookMutation, StaffWebhookFailure>> RotateAsync(Guid competitionId, Guid targetId, Guid actorId, CancellationToken ct);
    Task<StaffWebhookFailure?> DeleteAsync(Guid competitionId, Guid targetId, Guid actorId, CancellationToken ct);
    Task<OperationResult<Guid, StaffWebhookFailure>> TestAsync(Guid competitionId, Guid targetId, Guid actorId, CancellationToken ct);
    Task<OperationResult<StaffWebhookDeliveryPage, StaffWebhookFailure>> DiagnosticsAsync(Guid competitionId, Guid? targetId, Guid actorId, int offset, int limit, CancellationToken ct, bool descending = true);
    Task<OperationResult<StaffWebhookDeliveryView, StaffWebhookFailure>> TestStatusAsync(Guid competitionId, Guid targetId, Guid eventId, Guid actorId, CancellationToken ct);
    Task<OperationResult<StaffWorkItemPage, StaffWebhookFailure>> WorkItemsAsync(Guid competitionId, Guid actorId, int offset, int limit, bool pendingOnly, CancellationToken ct, bool descending = false);
    Task TickAsync(bool recovering, CancellationToken ct);
    Task<IReadOnlyList<DeliverStaffWebhook>> ClaimAsync(int limit, CancellationToken ct);
    Task<StaffWebhookPreparedDelivery> PrepareAsync(DeliverStaffWebhook command, CancellationToken ct);
    Task CompleteAsync(DeliverStaffWebhook command, int? status, bool delivered, bool permanent, DateTimeOffset? retryAfter, CancellationToken ct);
}

public sealed class ManageStaffWebhooks(IStaffWebhookStore store)
{
    public Task<OperationResult<StaffWebhookMutation, StaffWebhookFailure>> SaveAsync(SaveStaffWebhook command, CancellationToken ct) => store.SaveAsync(command, ct);
    public Task<OperationResult<StaffWebhookMutation, StaffWebhookFailure>> RotateAsync(Guid competitionId, Guid targetId, Guid actorId, CancellationToken ct) => store.RotateAsync(competitionId, targetId, actorId, ct);
    public Task<StaffWebhookFailure?> DeleteAsync(Guid competitionId, Guid targetId, Guid actorId, CancellationToken ct) => store.DeleteAsync(competitionId, targetId, actorId, ct);
    public Task<OperationResult<Guid, StaffWebhookFailure>> TestAsync(Guid competitionId, Guid targetId, Guid actorId, CancellationToken ct) => store.TestAsync(competitionId, targetId, actorId, ct);
}
