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
            var adminEmail = configuration?["SeedAdmin:Email"] ?? "admin@noctf.local";
            var adminUserName = configuration?["SeedAdmin:UserName"] ?? "admin";
            var adminPassword = configuration?["SeedAdmin:Password"];

            var existingAdmin = await db.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);
            if (existingAdmin is not null)
            {
                if (existingAdmin.Role != UserRole.Admin)
                    throw new SeedConfigurationException(
                        "SeedAdmin:Email belongs to an existing non-admin user; automatic promotion is forbidden.");
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
            var admin = new User
            {
                Id = Guid.NewGuid(),
                Email = adminEmail,
                UserName = adminUserName,
                PasswordHash = hasher.HashPassword(null!, adminPassword),
                Role = UserRole.Admin,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
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
