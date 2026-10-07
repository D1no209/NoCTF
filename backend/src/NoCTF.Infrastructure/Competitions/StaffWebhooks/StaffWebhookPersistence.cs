using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions.StaffWebhooks;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Competitions.StaffWebhooks;

public sealed class StaffWebhookStream : IConcurrencyTracked
{
    [Key] public Guid CompetitionId { get; set; }
    public long Sequence { get; set; }
    public long LatestBusinessSequence { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public sealed class StaffWebhookTarget : IConcurrencyTracked
{
    [Key] public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ApprovedById { get; set; }
    [MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(2048)] public string EndpointUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool Deleted { get; set; }
    public bool AuthorizationRevoked { get; set; }
    public Guid Generation { get; set; } = Guid.NewGuid();
    public byte[] SecretCiphertext { get; set; } = [];
    public byte[]? PreviousSecretCiphertext { get; set; }
    public DateTimeOffset? PreviousSecretValidUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset NextHeartbeatAt { get; set; }
    public DateTimeOffset? FailureSince { get; set; }
    public bool NeedsResync { get; set; }
    public Guid? ActiveSnapshotId { get; set; }
    public long SyncedThrough { get; set; }
    public List<StaffWebhookCategory> Categories { get; set; } = [];
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

[PrimaryKey(nameof(TargetId), nameof(Kind))]
public sealed class StaffWebhookCategory
{
    public Guid TargetId { get; set; }
    public StaffWorkItemKind Kind { get; set; }
}

// An allowlisted relational projection; protected submission/message fields never enter this model.
public sealed class StaffWorkItemSummary
{
    public StaffWorkItemKind Kind { get; set; }
    public Guid Id { get; set; }
    public CheatIncidentStatus? CheatStatus { get; set; }
    public CompetitionQuestionStatus? ConsultationStatus { get; set; }
    public TeamBanAppealStatus? AppealStatus { get; set; }
    public bool RequiresStaffAction { get; set; }
    public DateTimeOffset? ActionRequiredSince { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DetectedAt { get; set; }
    public Guid? TeamId { get; set; }
    [MaxLength(160)] public string? TeamName { get; set; }
    public Guid? RelatedTeamId { get; set; }
    [MaxLength(160)] public string? RelatedTeamName { get; set; }
    public Guid? ChallengeId { get; set; }
    [MaxLength(160)] public string? ChallengeTitle { get; set; }
    [MaxLength(64)] public string? Direction { get; set; }
    public GameplayFactFailureCode? ReasonCode { get; set; }
    public CompetitionQuestionSubject? Subject { get; set; }
    [MaxLength(160)] public string? ActorDisplayName { get; set; }
    public long LastChangedSequence { get; set; }
    public StaffWorkItemSummary Copy() => (StaffWorkItemSummary)MemberwiseClone();
}

[PrimaryKey(nameof(CompetitionId), nameof(Kind), nameof(ItemId))]
public sealed class StaffWebhookWorkItem
{
    public Guid CompetitionId { get; set; }
    public StaffWorkItemKind Kind { get; set; }
    public Guid ItemId { get; set; }
    public StaffWorkItemSummary Summary { get; set; } = new();
}

[Index(nameof(CompetitionId), nameof(Sequence), IsUnique = true)]
public sealed class StaffWebhookEvent
{
    [Key] public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public long Sequence { get; set; }
    public long LatestBusinessSequence { get; set; }
    public StaffWebhookEventKind Kind { get; set; }
    [MaxLength(160)] public string CompetitionTitle { get; set; } = string.Empty;
    public DateTimeOffset? DispatchedAt { get; set; }
    public StaffWorkItemChangeKind? ChangeKind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? TargetId { get; set; }
    public Guid? SnapshotId { get; set; }
    public StaffSnapshotReason? SnapshotReason { get; set; }
    public long? AsOfSequence { get; set; }
    public int? PageIndex { get; set; }
    public int? PageCount { get; set; }
    public List<StaffWebhookEventItem> Items { get; set; } = [];
}

[PrimaryKey(nameof(EventId), nameof(Position))]
public sealed class StaffWebhookEventItem
{
    public Guid EventId { get; set; }
    public int Position { get; set; }
    public StaffWorkItemSummary Summary { get; set; } = new();
}

[PrimaryKey(nameof(EventId), nameof(TargetId))]
[Index(nameof(State), nameof(NextAttemptAt))]
public sealed class StaffWebhookDelivery : IConcurrencyTracked
{
    public Guid EventId { get; set; }
    public Guid TargetId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid Generation { get; set; }
    public StaffWebhookDeliveryState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid AttemptToken { get; set; }
    public DateTimeOffset? PreparedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int Attempts { get; set; }
    public int? LastStatusCode { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

internal sealed class StaffWebhookPersistenceConfiguration : IEntityTypeConfiguration<StaffWebhookStream>,
    IEntityTypeConfiguration<StaffWebhookTarget>, IEntityTypeConfiguration<StaffWebhookWorkItem>,
    IEntityTypeConfiguration<StaffWebhookEvent>, IEntityTypeConfiguration<StaffWebhookEventItem>, IEntityTypeConfiguration<StaffWebhookDelivery>
{
    public void Configure(EntityTypeBuilder<StaffWebhookStream> builder)
    {
        builder.ToTable("competition_staff_webhook_streams");
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany().HasForeignKey(value => value.CompetitionId);
    }
    public void Configure(EntityTypeBuilder<StaffWebhookTarget> builder)
    {
        builder.ToTable("competition_staff_webhook_targets");
        builder.HasIndex(value => new { value.CompetitionId, value.Deleted });
        builder.HasMany(value => value.Categories).WithOne().HasForeignKey(value => value.TargetId);
        builder.Navigation(value => value.Categories).AutoInclude();
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany().HasForeignKey(value => value.CompetitionId);
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany().HasForeignKey(value => value.ApprovedById).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<StaffWebhookWorkItem> builder)
    {
        builder.ToTable("competition_staff_webhook_work_items");
        builder.OwnsOne(value => value.Summary);
        builder.Navigation(value => value.Summary).IsRequired();
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany().HasForeignKey(value => value.CompetitionId);
    }
    public void Configure(EntityTypeBuilder<StaffWebhookEvent> builder)
    {
        builder.ToTable("competition_staff_webhook_events");
        builder.HasMany(value => value.Items).WithOne().HasForeignKey(value => value.EventId);
        builder.Navigation(value => value.Items).AutoInclude();
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany().HasForeignKey(value => value.CompetitionId);
    }
    public void Configure(EntityTypeBuilder<StaffWebhookEventItem> builder)
    {
        builder.ToTable("competition_staff_webhook_event_items");
        builder.OwnsOne(value => value.Summary);
        builder.Navigation(value => value.Summary).IsRequired();
    }
    public void Configure(EntityTypeBuilder<StaffWebhookDelivery> builder)
    {
        builder.ToTable("competition_staff_webhook_deliveries");
        builder.HasOne<StaffWebhookEvent>().WithMany().HasForeignKey(value => value.EventId);
        builder.HasOne<StaffWebhookTarget>().WithMany().HasForeignKey(value => value.TargetId);
    }
}
