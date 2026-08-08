using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Persistence.Configurations.Identity;

internal sealed class AccountTokenConfiguration : IEntityTypeConfiguration<AccountToken>
{
    public void Configure(EntityTypeBuilder<AccountToken> builder)
    {
        builder.ToTable("account_tokens", table =>
        {
            table.HasCheckConstraint("ck_account_tokens_expiry", "expires_at > created_at");
            table.HasCheckConstraint(
                "ck_account_tokens_consumed_at",
                "consumed_at IS NULL OR consumed_at >= created_at");
            table.HasCheckConstraint(
                "ck_account_tokens_invalidated_at",
                "invalidated_at IS NULL OR invalidated_at >= created_at");
            table.HasCheckConstraint(
                "ck_account_tokens_terminal_state",
                "consumed_at IS NULL OR invalidated_at IS NULL");
            table.HasCheckConstraint(
                "ck_account_tokens_email_not_invalidated",
                "kind <> 0 OR invalidated_at IS NULL");
            table.HasCheckConstraint("ck_account_tokens_hash_length", "octet_length(token_sha256) = 32");
        });
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Kind).HasConversion<short>();
        builder.HasIndex(token => token.TokenSha256).IsUnique();
        builder.HasIndex(token => new { token.UserId, token.Kind, token.CreatedAt });
        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
