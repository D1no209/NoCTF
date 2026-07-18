using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Persistence.Configurations.Identity;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.UserName).HasMaxLength(64);
        builder.Property(user => user.NormalizedUserName).HasMaxLength(64);
        builder.Property(user => user.Email).HasMaxLength(254);
        builder.Property(user => user.NormalizedEmail).HasMaxLength(254);
        builder.HasIndex(user => user.NormalizedUserName).IsUnique();
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
    }
}
