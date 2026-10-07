using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity.Passkeys;

namespace NoCTF.Infrastructure.Authentication.Passkeys;

internal sealed class UserPasskeyConfiguration : IEntityTypeConfiguration<UserPasskey>
{
    public void Configure(EntityTypeBuilder<UserPasskey> builder)
    {
        builder.HasIndex(value => value.CredentialId).IsUnique();
        builder.HasIndex(value => new { value.UserId, value.RelyingPartyId });
        builder.Property(value => value.SignCount).HasConversion<long>();
        builder.HasMany(value => value.Transports).WithOne().HasForeignKey(value => value.UserPasskeyId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(value => value.Transports).AutoInclude();
    }
}
internal sealed class UserPasskeyTransportConfiguration : IEntityTypeConfiguration<UserPasskeyTransport>
{
    public void Configure(EntityTypeBuilder<UserPasskeyTransport> builder) => builder.HasKey(value => new { value.UserPasskeyId, value.Position });
}
internal sealed class PasskeyCeremonyConfiguration : IEntityTypeConfiguration<PasskeyCeremony>
{
    public void Configure(EntityTypeBuilder<PasskeyCeremony> builder)
    {
        builder.HasIndex(value => value.ExpiresAt);
        builder.HasIndex(value => new { value.UserId, value.Purpose });
    }
}
