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
        builder.Property(notification => notification.Kind).HasConversion<short>();
        builder.Property(notification => notification.RelatedType).HasConversion<short>();
        builder.Property(notification => notification.ContentJson).HasColumnType("jsonb");
        builder.HasIndex(notification => new
            { notification.TargetType, notification.TargetId, notification.SentAt, notification.Id });
        builder.HasIndex(notification => new
            { notification.SourceType, notification.SourceId, notification.SentAt, notification.Id })
            .HasFilter("source_id IS NOT NULL");
        builder.HasIndex(notification => new
            { notification.RelatedType, notification.RelatedId, notification.SentAt, notification.Id })
            .HasFilter("related_type IS NOT NULL");
        builder.HasIndex(notification => new
            { notification.ThreadRootId, notification.SentAt, notification.Id })
            .HasFilter("thread_root_id IS NOT NULL");
        builder.HasIndex(notification => notification.ReplyToId)
            .HasFilter("reply_to_id IS NOT NULL");
        builder.HasOne<Notification>().WithMany()
            .HasForeignKey(notification => notification.ThreadRootId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Notification>().WithMany()
            .HasForeignKey(notification => notification.ReplyToId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_notifications_source",
                "(source_type IN (0, 4) AND source_id IS NULL) OR (source_type IN (1, 2, 3) AND source_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_notifications_related_reference",
                "(related_type IS NULL) = (related_id IS NULL)");
            table.HasCheckConstraint(
                "ck_notifications_thread_root",
                "thread_root_id IS NULL OR thread_root_id <> id");
            table.HasCheckConstraint(
                "ck_notifications_platform_administrators_target",
                "target_type <> 4 OR target_id = 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid");
            table.HasCheckConstraint(
                "ck_notifications_content",
                "jsonb_typeof(content_json) = 'object' AND content_json ? 'schemaVersion'");
        });
    }
}
