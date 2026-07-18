using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NoCTF.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NoCtfDbContext>
{
    public NoCtfDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOCTF_POSTGRES")
            ?? "Host=localhost;Port=5432;Database=noctf;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new(options);
    }
}
