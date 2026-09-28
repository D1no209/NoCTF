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
        builder.Property(user => user.NormalizedEmail).HasMaxLength(320);
        builder.Property(user => user.Kind).HasConversion<short>();
        builder.Property(user => user.Role).HasConversion<short>();
        builder.Property(user => user.AccountStatus).HasConversion<short>();
        builder.HasIndex(user => user.NormalizedUserName).IsUnique();
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
        builder.HasOne(user => user.ExternalIdentity)
            .WithOne()
            .HasForeignKey<ExternalIdentity>(identity => identity.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(user => user.ExternalIdentity).AutoInclude();
        builder.HasOne(user => user.AvatarFile).WithMany()
            .HasForeignKey(user => user.AvatarFileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(user => user.ProfileCoverFile).WithMany()
            .HasForeignKey(user => user.ProfileCoverFileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(user => user.WallpaperFile).WithMany()
            .HasForeignKey(user => user.WallpaperFileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExternalIdentityConfiguration
    : IEntityTypeConfiguration<ExternalIdentity>
{
    public void Configure(EntityTypeBuilder<ExternalIdentity> builder)
    {
        builder.ToTable("external_identities");
        builder.HasKey(identity => identity.UserId);
        builder.Property(identity => identity.Protocol).HasConversion<short>();
        builder.HasIndex(identity => new { identity.ProviderId, identity.NormalizedSubject }).IsUnique();
    }
}
