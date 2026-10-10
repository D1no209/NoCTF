using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Persistence;

internal sealed class LiveSoloMediaGrantConfiguration : IEntityTypeConfiguration<LiveSoloMediaGrant>
{
    public void Configure(EntityTypeBuilder<LiveSoloMediaGrant> builder)
    {
        builder.ToTable("live_solo_media_grants");
        builder.HasIndex(x => new { x.MediaSessionId, x.UserId, x.Role }).IsUnique();
        builder.HasOne<LiveSoloMediaSession>().WithMany().HasForeignKey(x => x.MediaSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
