using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity.Mfa;

namespace NoCTF.Infrastructure.Authentication.Mfa;

internal sealed class UserTotpCredentialConfiguration : IEntityTypeConfiguration<UserTotpCredential>
{
    public void Configure(EntityTypeBuilder<UserTotpCredential> builder) => builder.HasIndex(value => value.UserId).IsUnique();
}

internal sealed class UserMfaRecoveryCodeConfiguration : IEntityTypeConfiguration<UserMfaRecoveryCode>
{
    public void Configure(EntityTypeBuilder<UserMfaRecoveryCode> builder) =>
        builder.HasIndex(value => new { value.UserId, value.BatchId, value.CodeSha256 }).IsUnique();
}

internal sealed class MfaChallengeConfiguration : IEntityTypeConfiguration<MfaChallenge>
{
    public void Configure(EntityTypeBuilder<MfaChallenge> builder)
    {
        builder.HasIndex(value => value.ExpiresAt);
        builder.HasIndex(value => new { value.UserId, value.Purpose });
    }
}
