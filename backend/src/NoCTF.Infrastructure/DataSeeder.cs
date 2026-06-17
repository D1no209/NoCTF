using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;

namespace NoCTF.Infrastructure;

public static class DataSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db)
    {
        // Only seed if database is accessible
        try
        {
            var existingAdmin = await db.Users.FirstOrDefaultAsync(u => u.Email == "admin@noctf.local");
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
                Email = "admin@noctf.local",
                UserName = "admin",
                PasswordHash = hasher.HashPassword(null!, "Admin@123456"),
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
