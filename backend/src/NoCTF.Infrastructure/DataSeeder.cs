using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.Core;

namespace NoCTF.Infrastructure;

public static class DataSeeder
{
    private const string DefaultAdminPassword = "Admin@123456";
    private sealed class SeedConfigurationException(string message) : Exception(message);

    public static async Task SeedAsync(
        ApplicationDbContext db,
        IConfiguration? configuration = null,
        bool allowDefaultAdminCredentials = false)
    {
        // Only seed if database is accessible
        try
        {
            var adminEmail = (configuration?["SeedAdmin:Email"] ?? "admin@noctf.local").Trim().ToLowerInvariant();
            var adminUserName = (configuration?["SeedAdmin:UserName"] ?? "admin").Trim();
            var adminPassword = configuration?["SeedAdmin:Password"];

            if (adminEmail.Length > UserInputLimits.EmailMaxLength ||
                adminUserName.Length is < UserInputLimits.UserNameMinLength or > UserInputLimits.UserNameMaxLength ||
                adminUserName.Any(char.IsWhiteSpace))
            {
                throw new SeedConfigurationException("SeedAdmin email or user name is invalid.");
            }

            var normalizedAdminEmail = adminEmail.ToLowerInvariant();
            var existingAdmin = db.Database.IsRelational()
                ? await db.Users.FirstOrDefaultAsync(
                    u => EF.Property<string>(u, "NormalizedEmail") == normalizedAdminEmail)
                : await db.Users.FirstOrDefaultAsync(
                    u => u.Email.ToLower() == normalizedAdminEmail);
            if (existingAdmin is not null)
            {
                if (existingAdmin.Role != UserRole.Admin)
                    throw new SeedConfigurationException(
                        "SeedAdmin:Email belongs to an existing non-admin user; automatic promotion is forbidden.");
                if (!existingAdmin.EmailVerifiedAt.HasValue)
                {
                    var recoveryTime = DateTime.UtcNow;
                    existingAdmin.EmailVerifiedAt = recoveryTime;
                    existingAdmin.UpdatedAt = recoveryTime;
                    await db.SaveChangesAsync();
                }
                return;
            }

            var hasAnyAdmin = await db.Users.AnyAsync(u => u.Role == UserRole.Admin);
            if (hasAnyAdmin) return;

            if (string.IsNullOrWhiteSpace(adminPassword))
            {
                if (!allowDefaultAdminCredentials)
                    throw new SeedConfigurationException("SeedAdmin:Password must be configured before seeding the initial administrator.");

                adminPassword = DefaultAdminPassword;
            }

            if (!allowDefaultAdminCredentials &&
                adminPassword.Equals(DefaultAdminPassword, StringComparison.Ordinal))
            {
                throw new SeedConfigurationException("SeedAdmin:Password must be changed before seeding the initial administrator.");
            }

            var hasher = new PasswordHasher<User>();
            var now = DateTime.UtcNow;
            var admin = new User
            {
                Id = Guid.NewGuid(),
                Email = adminEmail,
                UserName = adminUserName,
                PasswordHash = hasher.HashPassword(null!, adminPassword),
                Role = UserRole.Admin,
                EmailVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Users.Add(admin);
            await db.SaveChangesAsync();
        }
        catch (SeedConfigurationException)
        {
            throw;
        }
    }
}
