using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Persistence.Configurations.Identity;

internal sealed class AccountTokenConfiguration : IEntityTypeConfiguration<AccountToken>
{
    public void Configure(EntityTypeBuilder<AccountToken> builder)
    {
        builder.ToTable("account_tokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Kind).HasConversion<short>();
        builder.HasIndex(token => token.TokenSha256).IsUnique();
        builder.HasIndex(token => new { token.UserId, token.Kind, token.CreatedAt });
        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
