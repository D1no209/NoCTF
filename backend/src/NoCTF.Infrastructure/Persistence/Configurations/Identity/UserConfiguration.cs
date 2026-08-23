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
        builder.Property(user => user.Email).HasMaxLength(320);
        builder.Property(user => user.Kind).HasConversion<short>();
        builder.Property(user => user.Role).HasConversion<short>();
        builder.Property(user => user.AccountStatus).HasConversion<short>();
        builder.HasIndex(user => user.NormalizedUserName).IsUnique();
        builder.HasIndex(user => user.Email).IsUnique()
            .HasFilter("email <> ''");
        builder.HasOne(user => user.AvatarFile).WithMany()
            .HasForeignKey(user => user.AvatarFileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_users_bot_role",
            "\"kind\" <> 1 OR \"role\" IN (0, 1)"));
    }
}
