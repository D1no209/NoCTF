using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Persistence.Configurations.Identity;

internal sealed class RefreshSessionConfiguration : IEntityTypeConfiguration<RefreshSession>
{
    public void Configure(EntityTypeBuilder<RefreshSession> builder)
    {
        builder.ToTable("refresh_sessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.TokenHash).HasMaxLength(128);
        builder.HasIndex(session => session.TokenHash).IsUnique();
        builder.HasIndex(session => new { session.UserId, session.FamilyId });
    }
}
