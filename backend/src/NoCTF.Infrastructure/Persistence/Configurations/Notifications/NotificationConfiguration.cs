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
        builder.Property(notification => notification.Kind).HasConversion<short>();
        builder.Property(notification => notification.PayloadJson).HasColumnType("jsonb");
        builder.HasIndex(notification => new { notification.UserId, notification.CreatedAt, notification.Id });
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany()
            .HasForeignKey(notification => notification.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany()
            .HasForeignKey(notification => notification.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
