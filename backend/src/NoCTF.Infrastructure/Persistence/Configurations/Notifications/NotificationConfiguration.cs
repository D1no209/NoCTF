using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Notifications;

namespace NoCTF.Infrastructure.Persistence.Configurations.Notifications;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.SourceType).HasConversion<short>();
        builder.Property(notification => notification.TargetType).HasConversion<short>();
        var discriminator = builder.HasDiscriminator(notification => notification.Kind);
        foreach (var entry in NotificationGeneratedCatalog.Entries)
            discriminator.HasValue(entry.Leaf, entry.Kind);
        builder.Property(notification => notification.RelatedType).HasConversion<short>();
        builder.Property(notification => notification.QuestionSubject).HasConversion<short>();
        builder.Property(notification => notification.QuestionStatus).HasConversion<short>();
        builder.Property(notification => notification.PreviousQuestionStatus).HasConversion<short>();
        builder.Property(notification => notification.QuestionActorRole).HasConversion<short>();
        builder.Property(notification => notification.UserLifecycleAction).HasConversion<short>();
        builder.Property(notification => notification.SsoProtocol).HasConversion<short>();
        builder.Property(notification => notification.RuntimeState).HasConversion<short>();
        builder.Property(notification => notification.GameplayFactState).HasConversion<short>();
        builder.Property(notification => notification.GameplayFactResult).HasConversion<short>();
        builder.Property(notification => notification.GameplayFactFailureCode).HasConversion<short>();
        builder.HasMany(notification => notification.ReferenceCounts)
            .WithOne()
            .HasForeignKey(reference => reference.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(notification => notification.ReferenceCounts).AutoInclude();
        builder.HasIndex(notification => new
            { notification.TargetType, notification.TargetId, notification.SentAt, notification.Id });
        builder.HasIndex(notification => new
            { notification.SourceType, notification.SourceId, notification.SentAt, notification.Id });
        builder.HasIndex(notification => new
            { notification.RelatedType, notification.RelatedId, notification.SentAt, notification.Id });
        builder.HasIndex(notification => new
            { notification.ThreadRootId, notification.SentAt, notification.Id });
        builder.HasIndex(notification => notification.ReplyToId);
        builder.HasOne<Notification>().WithMany()
            .HasForeignKey(notification => notification.ThreadRootId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Notification>().WithMany()
            .HasForeignKey(notification => notification.ReplyToId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class NotificationReferenceCountConfiguration
    : IEntityTypeConfiguration<NotificationReferenceCount>
{
    public void Configure(EntityTypeBuilder<NotificationReferenceCount> builder)
    {
        builder.ToTable("notification_reference_counts");
        builder.HasKey(reference => new { reference.NotificationId, reference.ReferenceKind });
    }
}
