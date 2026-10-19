using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions.Blood;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

internal sealed class BloodAnnouncementConfiguration : IEntityTypeConfiguration<BloodAnnouncement>
{
    public void Configure(EntityTypeBuilder<BloodAnnouncement> builder)
    {
        builder.ToTable("competition_blood_announcements");
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => new { item.CompetitionChallengeId, item.Position }).IsUnique();
        builder.Property(item => item.Position).HasConversion<short>();
        builder.Property(item => item.ConcurrencyStamp).IsConcurrencyToken();
    }
}
