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
        builder.Property(user => user.ExternalIdentityProtocol).HasConversion<short>();
        builder.HasIndex(user => user.NormalizedUserName).IsUnique();
        builder.HasIndex(user => user.Email).IsUnique()
            .HasFilter("email <> ''");
        builder.HasIndex(user => new
            {
                user.ExternalIdentityProviderId,
                user.ExternalIdentitySubject
            })
            .IsUnique()
            .HasFilter("external_identity_provider_id IS NOT NULL");
        builder.HasOne(user => user.AvatarFile).WithMany()
            .HasForeignKey(user => user.AvatarFileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(user => user.ProfileCoverFile).WithMany()
            .HasForeignKey(user => user.ProfileCoverFileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(user => user.WallpaperFile).WithMany()
            .HasForeignKey(user => user.WallpaperFileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_users_wallpaper_enabled",
                "NOT \"wallpaper_enabled\" OR \"wallpaper_file_id\" IS NOT NULL");
            table.HasCheckConstraint(
                "ck_users_external_identity_complete",
                "(\"external_identity_provider_id\" IS NULL"
                + " AND \"external_identity_protocol\" IS NULL"
                + " AND \"external_identity_namespace\" IS NULL"
                + " AND \"external_identity_subject\" IS NULL"
                + " AND \"external_identity_bound_at\" IS NULL)"
                + " OR (\"external_identity_provider_id\" IS NOT NULL"
                + " AND \"external_identity_protocol\" IS NOT NULL"
                + " AND \"external_identity_namespace\" IS NOT NULL"
                + " AND \"external_identity_subject\" IS NOT NULL"
                + " AND \"external_identity_bound_at\" IS NOT NULL)");
        });
    }
}
