using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.Core;

namespace NoCTF.Infrastructure;

public static class DataSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, IConfiguration? configuration = null)
    {
        // Only seed if database is accessible
        try
        {
            var adminEmail = configuration?["SeedAdmin:Email"] ?? "admin@noctf.local";
            var adminUserName = configuration?["SeedAdmin:UserName"] ?? "admin";
            var adminPassword = configuration?["SeedAdmin:Password"] ?? "Admin@123456";

            var existingAdmin = await db.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);
            if (existingAdmin is not null)
            {
                // If admin@noctf.local exists but is not Admin, promote it
                if (existingAdmin.Role != UserRole.Admin)
                {
                    existingAdmin.Role = UserRole.Admin;
                    await db.SaveChangesAsync();
                }
                return;
            }

            var hasAnyAdmin = await db.Users.AnyAsync(u => u.Role == UserRole.Admin);
            if (hasAnyAdmin) return;

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
        catch
        {
            // Ignore seeding errors (e.g., database not yet migrated)
        }
    }
}
