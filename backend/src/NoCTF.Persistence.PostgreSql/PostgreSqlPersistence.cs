using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Persistence.PostgreSql;

public static class PostgreSqlPersistence
{
    public static IServiceCollection AddNoCtfDatabaseProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "PostgreSql";
        if (!string.Equals(provider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Database provider '{provider}' is not installed. This build supports PostgreSql only.");
        var connectionString = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
        void Configure(DbContextOptionsBuilder options) => options
                .UseNpgsql(connectionString, npgsql => npgsql
                    .MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName)
                    // Aggregate decisions keep one statement's snapshot; bounded read projections opt into splitting.
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery))
                .UseSnakeCaseNamingConvention();
        services.AddDbContext<NoCtfDbContext>(Configure,
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContextFactory<NoCtfDbContext>(Configure);
        return services;
    }
}

public sealed class PostgreSqlDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<NoCtfDbContext>
{
    public NoCtfDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOCTF_POSTGRES")
            ?? "Host=localhost;Port=5432;Database=noctf;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(
                typeof(PostgreSqlPersistence).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new(options);
    }
}
