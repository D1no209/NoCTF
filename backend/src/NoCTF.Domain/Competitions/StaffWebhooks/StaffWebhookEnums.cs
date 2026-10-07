namespace NoCTF.Domain.Competitions.StaffWebhooks;

public enum StaffWorkItemKind : short { CheatIncident, Consultation, BanAppeal }
public enum StaffWebhookEventKind : short { WorkItemCreated, WorkItemUpdated, PendingSnapshot, Heartbeat, SubscriptionDisabled, Test }
public enum StaffWorkItemChangeKind : short { Created, ParticipantMessage, StaffReply, StatusChanged, MetadataChanged }
public enum StaffSnapshotReason : short { Initial, Reenabled, Resync }
public enum StaffWebhookDeliveryState : short { Pending, InFlight, Delivered, Suppressed, DeadLetter }
public enum StaffWebhookFailure : short { NotFound, Forbidden, InvalidName, InvalidEndpoint, InvalidCategories, DuplicateEndpoint, Conflict }
