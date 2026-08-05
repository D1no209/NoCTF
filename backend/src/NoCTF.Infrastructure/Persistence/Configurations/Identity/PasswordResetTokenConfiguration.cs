using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Persistence.Configurations.Identity;

internal sealed class PasswordResetTokenConfiguration
    : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");
        builder.HasKey(token => token.Id);
        builder.HasIndex(token => token.TokenSha256).IsUnique();
        builder.HasIndex(token => new { token.UserId, token.CreatedAt });
        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
